import React, { useCallback, useEffect, useRef, useState } from 'react';
import './QrScanner.css';
import { Alert, Spin, Typography } from 'antd';
import jsQR from 'jsqr';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

/**
 * The camera side of scanning: a live preview, and the text of whatever QR code it finds.
 *
 * <para>
 * Decoding is two-tier on purpose. Where the platform provides `BarcodeDetector` — which is
 * what an Android WebView has, and what Chrome has anywhere — the frame is handed to it. Where
 * it does not (Firefox, Safari, a desktop browser), the frame is drawn to an offscreen canvas
 * and read with a small pure-JS decoder. Neither tier is a fallback for the *other* platform's
 * bug: both are implemented and both are exercised by the same call site, so a device that has
 * only one of them still scans.
 * </para>
 *
 * <para>
 * What this component deliberately does not do: decide what a code means. It calls `onDecode`
 * with the raw text and stops reading for a moment, so a tag held in frame cannot fire twice
 * while the caller is navigating. Meaning belongs to the pure resolver
 * (`offline/scanResolve.ts`), which is where the farm boundary is enforced.
 * </para>
 */

/** A camera failure the user needs to be told about, in words that suggest a next step. */
type ScannerFailure = 'unsupported' | 'denied' | 'noCamera' | 'failed';

/** The parts of the platform's detector this file uses. Structural, so no DOM lib assumptions. */
interface DetectedBarcode {
  rawValue: string;
}
interface BarcodeDetectorLike {
  detect: (source: CanvasImageSource) => Promise<DetectedBarcode[]>;
}
type BarcodeDetectorCtor = new (options?: { formats?: string[] }) => BarcodeDetectorLike;

const nativeDetectorCtor = (): BarcodeDetectorCtor | null => {
  const candidate = (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector;
  return typeof candidate === 'function' ? (candidate as BarcodeDetectorCtor) : null;
};

/**
 * How often a frame may be decoded.
 *
 * The preview runs at the display's rate, but decoding every frame would burn a field phone's
 * battery to answer the same question faster than a person can move the tag into view. Four
 * looks a second is comfortably faster than the hand holding the phone.
 */
const DECODE_INTERVAL_MS = 250;

/** The canvas width decoding works at: enough for a QR code at arm's length, cheap to scan. */
const DECODE_WIDTH = 640;

export interface QrScannerProps {
  /** Called with the raw text of each code read. */
  onDecode: (text: string) => void;
  /** Whether the camera should be running. */
  active: boolean;
  /**
   * Changing this value lets the scanner read again after `onDecode` was refused.
   *
   * A refused scan is a normal event in a pen — the wrong tag, a feed bag, a code from another
   * farm — so the caller has to be able to say "keep looking" without tearing the camera down
   * and asking the user to reopen the dialog.
   */
  resumeToken?: number;
}

const QrScanner: React.FC<QrScannerProps> = ({ onDecode, active, resumeToken = 0 }) => {
  const { t } = useTranslation('animals');
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const frameRef = useRef<number | null>(null);
  const lastDecodeRef = useRef(0);
  /** Set once a code has been read: the loop keeps the preview alive but stops reporting. */
  const handledRef = useRef(false);

  const [failure, setFailure] = useState<ScannerFailure | null>(null);
  const [starting, setStarting] = useState(false);

  // The caller refusing a code re-arms the reader without restarting the stream.
  useEffect(() => {
    handledRef.current = false;
  }, [resumeToken]);

  const stopCamera = useCallback(() => {
    if (frameRef.current !== null) {
      cancelAnimationFrame(frameRef.current);
      frameRef.current = null;
    }
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    const video = videoRef.current;
    if (video) video.srcObject = null;
  }, []);

  const startCamera = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      // No camera API at all: an insecure origin, an old WebView, or a browser that withholds
      // it. A message that says so is better than an empty box.
      setFailure('unsupported');
      return;
    }

    setStarting(true);
    setFailure(null);
    handledRef.current = false;

    try {
      const stream = await (async () => {
        try {
          // The rear camera, when the device has one: this is a scanner pointed at a tag, not
          // a selfie. `ideal` rather than `exact` so a device without a rear camera still starts.
          return await navigator.mediaDevices.getUserMedia({
            video: { facingMode: { ideal: 'environment' } },
            audio: false,
          });
        } catch (error) {
          // A device that refuses the constraint rather than ignoring it must not lose the
          // scanner: any camera is better than none.
          if ((error as { name?: string }).name !== 'OverconstrainedError') throw error;
          return await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        }
      })();

      streamRef.current = stream;

      const video = videoRef.current;
      if (!video) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }

      video.srcObject = stream;
      // Some platforms will not begin playing without an explicit call, and `playsInline` is
      // what keeps a mobile browser from taking the video full screen. A refused autoplay is
      // not a broken scanner: the stream is live and the frames are still readable, so the
      // failure is swallowed rather than turned into an error message.
      try {
        await video.play();
      } catch {
        // Deliberately ignored.
      }
    } catch (error) {
      const name = (error as { name?: string }).name;
      if (name === 'NotAllowedError' || name === 'SecurityError') setFailure('denied');
      else if (name === 'NotFoundError' || name === 'OverconstrainedError' || name === 'DevicesNotFoundError') setFailure('noCamera');
      else setFailure('failed');
      stopCamera();
    } finally {
      setStarting(false);
    }
  }, [stopCamera]);

  /** One look at the current frame; returns the code's text, or null when there is none. */
  const decodeFrame = useCallback(async (video: HTMLVideoElement): Promise<string | null> => {
    const Detector = nativeDetectorCtor();

    if (Detector) {
      const detector = new Detector({ formats: ['qr_code'] });
      const found = await detector.detect(video);
      return found.length > 0 ? found[0].rawValue ?? null : null;
    }

    const canvas = canvasRef.current;
    if (!canvas || !video.videoWidth) return null;

    const width = Math.min(DECODE_WIDTH, video.videoWidth);
    const height = Math.round((video.videoHeight / video.videoWidth) * width);
    canvas.width = width;
    canvas.height = height;

    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (!context) return null;

    context.drawImage(video, 0, 0, width, height);
    const image = context.getImageData(0, 0, width, height);
    // No inversion attempt: an inverted code is not how these labels are printed, and trying
    // both costs double the work on every frame that has no code in it — which is most of them.
    return jsQR(image.data, image.width, image.height, { inversionAttempts: 'dontInvert' })?.data ?? null;
  }, []);

  useEffect(() => {
    if (!active) {
      stopCamera();
      return;
    }

    void startCamera();
    return stopCamera;
  }, [active, startCamera, stopCamera]);

  useEffect(() => {
    if (!active) return;

    const tick = () => {
      frameRef.current = requestAnimationFrame(tick);

      const video = videoRef.current;
      if (!video || video.readyState < 2 || handledRef.current) return;

      const now = Date.now();
      if (now - lastDecodeRef.current < DECODE_INTERVAL_MS) return;
      lastDecodeRef.current = now;

      void decodeFrame(video)
        .then((text) => {
          if (!text || handledRef.current) return;
          // Stops this scan from firing again while the caller navigates or opens a dialog;
          // the next scan resets it, so a wrong code can simply be re-read.
          handledRef.current = true;
          onDecode(text);
        })
        .catch(() => {
          // A decoder that threw on one frame is not a broken scanner: the next frame is a
          // different picture, and a device that reports every failure would only be noise.
        });
    };

    frameRef.current = requestAnimationFrame(tick);
    return () => {
      if (frameRef.current !== null) {
        cancelAnimationFrame(frameRef.current);
        frameRef.current = null;
      }
    };
  }, [active, decodeFrame, onDecode]);

  const failureMessage: Record<ScannerFailure, string> = {
    unsupported: t('noCameraForTheApp'),
    denied: t('cameraAccessWasRefused'),
    noCamera: t('noCameraAvailable'),
    failed: t('theCameraCouldNotBeStarted'),
  };

  return (
    <div>
      {failure ? (
        <Alert
          type="warning"
          showIcon
          message={failureMessage[failure]}
          // The words the user can act on: a refused camera is usually a setting, not a
          // broken phone.
          description={t('allowTheCameraForThisSiteOrApp')}
        />
      ) : (
        <>
          {/*
            `muted` is not cosmetic: an unmuted video element is what makes a mobile browser
            refuse to autoplay. The frame is sized by CSS so the preview fills the dialog.
          */}
          <video
            ref={videoRef}
            muted
            playsInline
            autoPlay
            className="fms-scan-preview"
          />
          <canvas ref={canvasRef} style={{ display: 'none' }} aria-hidden="true" />
          {starting && <Spin style={{ display: 'block', margin: '12px auto' }} />}
          <Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            {t('pointTheCameraAtTheAnimalsQrCode')}
          </Text>
        </>
      )}
    </div>
  );
};

export default QrScanner;
