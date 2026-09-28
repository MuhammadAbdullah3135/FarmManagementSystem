import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import QrScanner from './QrScanner';

/**
 * The camera half of scanning, which is the one part of this feature that cannot be asserted
 * from a unit test's point of view: a device either has a camera that the user allows, or it
 * does not, and each of those has to produce words a person can act on rather than a blank box
 * or a dead dialog.
 *
 * Nothing here decodes an image — decoding is exercised through the flow tests with the camera
 * replaced, and a fabricated JPEG proves nothing about a real tag.
 */

const setMediaDevices = (value: unknown) => {
  Object.defineProperty(navigator, 'mediaDevices', { value, configurable: true });
};

const cameraError = (name: string) => Object.assign(new Error(name), { name });

const getUserMedia = () => (navigator.mediaDevices.getUserMedia as ReturnType<typeof vi.fn>);

afterEach(() => {
  setMediaDevices(undefined);
});

describe('QrScanner without a usable camera', () => {
  it('says the camera is unavailable when the platform has no camera API at all', async () => {
    // An insecure origin, or an old WebView: nothing to open, and the user cannot fix it — but
    // they can use the app's other ways of finding the animal.
    setMediaDevices(undefined);

    render(<QrScanner active onDecode={() => undefined} />);

    expect(await screen.findByText('The camera is not available here.')).toBeInTheDocument();
  });

  it('tells the user what to change when permission is refused', async () => {
    setMediaDevices({ getUserMedia: vi.fn().mockRejectedValue(cameraError('NotAllowedError')) });

    render(<QrScanner active onDecode={() => undefined} />);

    expect(await screen.findByText('Camera access was refused.')).toBeInTheDocument();
    expect(
      screen.getByText('Allow the camera for this site or app and try again.'),
    ).toBeInTheDocument();
  });

  it('distinguishes a device with no camera from a refused one', async () => {
    setMediaDevices({ getUserMedia: vi.fn().mockRejectedValue(cameraError('NotFoundError')) });

    render(<QrScanner active onDecode={() => undefined} />);

    expect(
      await screen.findByText('This device has no camera available to the app.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Camera access was refused.')).not.toBeInTheDocument();
  });

  it('keeps looking when the browser ignores the rear-camera constraint', async () => {
    // A desktop or an emulator refuses `facingMode` outright. Asking again without it gets a
    // camera; treating it as a failure would leave a device that can scan unable to.
    const getUserMediaMock = vi
      .fn()
      .mockRejectedValueOnce(cameraError('OverconstrainedError'))
      .mockResolvedValue({ getTracks: () => [] });
    setMediaDevices({ getUserMedia: getUserMediaMock });

    render(<QrScanner active onDecode={() => undefined} />);

    await waitFor(() => expect(getUserMediaMock).toHaveBeenCalledTimes(2));
    expect(getUserMediaMock).toHaveBeenLastCalledWith({ video: true, audio: false });
    expect(screen.queryByText('This device has no camera available to the app.')).not.toBeInTheDocument();
  });
});

describe('QrScanner with a camera', () => {
  it('opens the rear camera and releases it when the scanner closes', async () => {
    const stop = vi.fn();
    setMediaDevices({
      getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop }] }),
    });

    const { unmount } = render(<QrScanner active onDecode={() => undefined} />);

    await waitFor(() => expect(getUserMedia()).toHaveBeenCalledTimes(1));
    expect(getUserMedia()).toHaveBeenCalledWith({
      video: { facingMode: { ideal: 'environment' } },
      audio: false,
    });
    expect(await screen.findByText("Point the camera at the animal's QR code.")).toBeInTheDocument();

    // The track is a live camera: leaving it running behind a closed dialog is what drains a
    // phone's battery and keeps the privacy indicator on.
    unmount();
    await waitFor(() => expect(stop).toHaveBeenCalled());
  });
});
