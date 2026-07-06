// Mirrors services/search/Models/UsageResponse.cs exactly. ASP.NET Core
// serializes with JsonSerializerDefaults.Web -> camelCase. See
// docs/log-dashboard.md section 5/6.

export interface UsageSummary {
  totalCostUsd: number;
  totalTokens: number;
  totalCalls: number;
  byModel: UsageBreakdown[];
  byService: UsageBreakdown[];
}

export interface UsageBreakdown {
  key: string;
  costUsd: number;
  tokens: number;
  calls: number;
}

export interface UsageTimeSeriesPoint {
  bucketStart: string;
  costUsd: number;
  tokens: number;
  calls: number;
}

export interface UsageLogPage {
  entries: UsageLogEntry[];
  hasMore: boolean;
}

export interface UsageLogEntry {
  id: number;
  occurredAt: string;
  serviceName: string;
  operation?: string | null;
  model: string;
  promptTokens?: number | null;
  completionTokens?: number | null;
  totalTokens?: number | null;
  isEstimated: boolean;
  costUsd?: number | null;
  sessionId?: string | null;
}

export interface UsageLogFilters {
  service?: string | null;
  model?: string | null;
  from?: string | null;
  to?: string | null;
}
