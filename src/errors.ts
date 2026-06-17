export type WorkerErrorCategory = "com" | "solidworks" | "validation" | "worker" | "timeout";

export interface WorkerError {
  code: string;
  message: string;
  category: WorkerErrorCategory;
  hresult?: string;
  swErrorCode?: number;
  swErrorName?: string;
  comInterface?: string;
  context?: Record<string, unknown>;
  remediation?: string[];
  docLink?: string;
  causedBy?: WorkerError;
}

export class SolidWorksWorkerError extends Error {
  readonly workerError: WorkerError;

  constructor(workerError: WorkerError) {
    super(workerError.message);
    this.name = "SolidWorksWorkerError";
    this.workerError = workerError;
  }
}

export function parseWorkerError(value: unknown): WorkerError | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  const candidate = value as Partial<WorkerError>;
  if (typeof candidate.code !== "string" || typeof candidate.message !== "string") {
    return null;
  }

  return {
    code: candidate.code,
    message: candidate.message,
    category: (candidate.category as WorkerErrorCategory) ?? "worker",
    hresult: candidate.hresult,
    swErrorCode: candidate.swErrorCode,
    swErrorName: candidate.swErrorName,
    comInterface: candidate.comInterface,
    context: candidate.context,
    remediation: candidate.remediation,
    docLink: candidate.docLink,
    causedBy: candidate.causedBy,
  };
}

export function formatErrorForMcp(error: unknown): string {
  if (error instanceof SolidWorksWorkerError) {
    return JSON.stringify(error.workerError, null, 2);
  }

  const parsed = parseWorkerError(
    error instanceof Error && "workerError" in error
      ? (error as SolidWorksWorkerError).workerError
      : error,
  );
  if (parsed) {
    return JSON.stringify(parsed, null, 2);
  }

  return error instanceof Error ? error.message : String(error);
}
