import api from './axios';
import { farmUrl } from './farmApi';

/** One file inside the export archive, as the manifest describes it. */
export interface FarmExportFile {
  fileName: string;
  entity: string;
  rowCount: number;
  /** The CSV's header row, in order. */
  columns: string[];
  /** True when this file can be fed back to its importer with no column mapping. */
  reimportable: boolean;
}

/** A farm-adjacent table deliberately left out, and the reason the archive gives. */
export interface FarmExportExclusion {
  entity: string;
  reason: string;
}

/**
 * The archive's own manifest, shipped inside the ZIP and returned by the status
 * endpoint. The page renders from this rather than from hard-coded copy, so what
 * the user is told about the archive is exactly what the archive says about
 * itself.
 */
export interface FarmExportManifest {
  schemaVersion: number;
  farmId: string;
  farmName: string;
  generatedAtUtc: string;
  generator: string;
  archiveFormat: string;
  totalRowCount: number;
  files: FarmExportFile[];
  excludedEntities: FarmExportExclusion[];
  /** Disclosed limitations: raw-only, no uploaded file bytes, no duplicate rule for money. */
  notes: string[];
}

/** The export's state. `status` describes the build; `isReady` describes the artifact. */
export interface FarmExport {
  id: string;
  farmId: string;
  /** Queued | Running | Completed | Failed */
  status: string;
  requestedByUserId: string;
  requestedAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  fileName?: string | null;
  sizeBytes?: number | null;
  error?: string | null;
  /** An archive exists and can be downloaded, whatever the current build is doing. */
  isReady: boolean;
  manifest?: FarmExportManifest | null;
}

export const farmExportApi = {
  /** The farm's export state. Null means it has never been exported. */
  status: () => api.get<FarmExport | null>(farmUrl('/export')),

  /**
   * Queues a build. Returns in one round trip regardless of the farm's size: the
   * work runs in the background and this answers with the record.
   */
  request: () => api.post<FarmExport>(farmUrl('/export')),

  /** The archive itself, as a Blob. The filename comes from the response headers. */
  download: () => api.get<Blob>(farmUrl('/export/download'), { responseType: 'blob' }),
};

/** The archive's download name, taken from the response rather than guessed. */
export const fileNameFrom = (headers: Record<string, unknown>): string => {
  const disposition = headers['content-disposition'];
  const match =
    typeof disposition === 'string'
      ? /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
      : null;

  return match?.[1] ?? 'farm-export.zip';
};
