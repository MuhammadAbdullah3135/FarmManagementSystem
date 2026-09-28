import React, { useCallback, useState } from 'react';
import { Alert, Button, Modal, Space, Tooltip, Typography } from 'antd';
import { QrcodeOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import QrScanner from './QrScanner';
import SyncAgeLabel from './SyncAgeLabel';
import { animalsApi } from '../api/animals';
import { useAnimalLookup } from '../offline/useAnimalLookup';
import { useOfflineStore } from '../offline/connectivity';

const { Text } = Typography;

/**
 * Scanning an animal's tag, from anywhere in the app.
 *
 * <para>
 * The flow is: read a code, resolve it against the rows this device holds for the *active
 * farm*, and open the animal. Resolution never invents an identity — it either matches a row
 * the device already has or it fails — so a label from another farm cannot be made to open
 * something here, and a scan works with no connection.
 * </para>
 *
 * <para>
 * Online there is a second chance: an id that matches nothing locally is verified against the
 * API before the user is told it is unknown. That is what keeps the device's bounded lookup
 * (a first page of animals) from turning into a wrong answer — the cache is the fast path, and
 * the server, which scopes every read to the farm, is the authority.
 * </para>
 */
const ScanDialog: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const { t } = useTranslation('animals');
  const navigate = useNavigate();
  const isOnline = useOfflineStore((state) => state.isOnline);
  const lookup = useAnimalLookup();

  const [problem, setProblem] = useState<string | null>(null);
  // Bumped whenever a code is refused, so the scanner keeps looking instead of waiting for the
  // dialog to be reopened.
  const [resumeToken, setResumeToken] = useState(0);

  const handleDecode = useCallback(
    async (text: string) => {
      const outcome = lookup.scan(text);

      if (outcome.status === 'found') {
        onClose();
        navigate(`/dashboard/animals/${outcome.animal.id}`);
        return;
      }

      if (outcome.status === 'notALabel') {
        setProblem(t('thatCodeIsNotAnAnimalTag'));
        setResumeToken((token) => token + 1);
        return;
      }

      if (isOnline) {
        try {
          // Confirms the id is an animal *of this farm* before navigating: the detail page
          // would otherwise 404 and bounce the user back to the list with no explanation.
          await animalsApi.get(outcome.animalId);
          onClose();
          navigate(`/dashboard/animals/${outcome.animalId}`);
          return;
        } catch (error) {
          const status = (error as { response?: { status?: number } })?.response?.status;
          setProblem(status === 404 ? t('scanUnknownAnimal') : t('couldNotCheckThatTag'));
        }
      } else {
        setProblem(t('scanUnknownAnimalOffline'));
      }

      setResumeToken((token) => token + 1);
    },
    [isOnline, lookup, navigate, onClose, t],
  );

  return (
    <Modal
      open={open}
      title={t('scanAnimalTag')}
      onCancel={onClose}
      footer={null}
      width={480}
      destroyOnClose
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        {lookup.isEmptyOffline ? (
          // Nothing stored and no connection: no scan can resolve, because there is nothing to
          // resolve against. Saying so beats starting a camera that cannot succeed.
          <Alert
            type="warning"
            showIcon
            message={t('noAnimalsAreStoredOnThisDevice')}
            description={t('connectOnceToDownloadTheFarmListThen')}
          />
        ) : (
          <>
            {problem && <Alert type="warning" showIcon message={problem} />}

            {/* Mounted only while the dialog is open, so the camera is never running behind it. */}
            {open && <QrScanner active onDecode={handleDecode} resumeToken={resumeToken} />}

            {!isOnline && (
              <Text type="secondary">{t('offlineScanningOnlyKnowsTheAnimals')}</Text>
            )}

            {lookup.lastSyncedAt && <SyncAgeLabel lastSyncedAt={lookup.lastSyncedAt} />}
          </>
        )}
      </Space>
    </Modal>
  );
};

export interface AnimalScanButtonProps {
  /**
   * `icon` for the app header, where space is what the farm selector and the queue badge are
   * competing for; `default` for a page's action row.
   */
  variant?: 'icon' | 'default';
}

/**
 * The scan action itself: a button that owns its dialog.
 *
 * One component for both call sites rather than a hook plus two buttons, so the header and the
 * animals list cannot drift into two slightly different scan flows.
 */
export const AnimalScanButton: React.FC<AnimalScanButtonProps> = ({ variant = 'default' }) => {
  const { t } = useTranslation('animals');
  const [open, setOpen] = useState(false);

  return (
    <>
      {variant === 'icon' ? (
        <Tooltip title={t('scanAnimalTag')}>
          <Button
            type="text"
            aria-label={t('scanAnimalTag')}
            icon={<QrcodeOutlined style={{ fontSize: 16 }} />}
            onClick={() => setOpen(true)}
          />
        </Tooltip>
      ) : (
        <Button icon={<QrcodeOutlined />} onClick={() => setOpen(true)}>
          {t('scanAnimalTag')}
        </Button>
      )}

      <ScanDialog open={open} onClose={() => setOpen(false)} />
    </>
  );
};

export default AnimalScanButton;
