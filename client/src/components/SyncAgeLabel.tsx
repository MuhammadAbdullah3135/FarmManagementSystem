import React from 'react';
import { Typography } from 'antd';
import { formatSyncAge } from '../offline/syncAge';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

export interface SyncAgeLabelProps {
  /** When the rows on screen were last read from the API, or null when unknown. */
  lastSyncedAt: string | null;
}

/**
 * Says, on the view itself, how old the data on screen is.
 *
 * The offline banner is global and describes the device; this sits next to the rows
 * and describes *them*. Requirement of the offline work (4.5.2): a cached view must
 * never pretend to be live, and "the banner was up at the top of the page" is not the
 * same as the table telling the truth about itself.
 *
 * Renders nothing when the view has no synced data to speak of (a filtered or
 * paginated view that is deliberately not cached), so the label always means
 * something when it appears.
 */
const SyncAgeLabel: React.FC<SyncAgeLabelProps> = ({ lastSyncedAt }) => {const { t } = useTranslation('common'); 
  if (!lastSyncedAt) return null;

  return (
    <Text type="secondary" style={{ fontSize: 12 }}>
      {t('synced')} {formatSyncAge(lastSyncedAt)}
    </Text>
  );
};

export default SyncAgeLabel;
