export type FeedbackCategory =
  | 'ProductQuality'
  | 'CustomerService'
  | 'Pricing'
  | 'Delivery'
  | 'Website'
  | 'Other';

export type SentimentType = 'Positive' | 'Neutral' | 'Negative';

export interface FeedbackItem {
  id: number;
  customerName: string;
  customerEmail?: string;
  category: FeedbackCategory;
  sentiment: SentimentType;
  rating: number;
  comment: string;
  createdAt: string;
  productId?: string;
  region?: string;
  isResolved: boolean;
}

export interface FeedbackCreateDto {
  customerName: string;
  customerEmail?: string;
  category: FeedbackCategory;
  rating: number;
  comment: string;
  productId?: string;
  region?: string;
}

export interface FeedbackUpdateDto {
  category?: FeedbackCategory;
  rating?: number;
  comment?: string;
  isResolved?: boolean;
}

export interface PagedResult<T> {
  data: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface FeedbackSummary {
  totalCount: number;
  averageRating: number;
  positiveCount: number;
  neutralCount: number;
  negativeCount: number;
  resolvedCount: number;
  countByCategory: Record<string, number>;
  countByRegion: Record<string, number>;
  avgRatingByCategory: Record<string, number>;
}

export interface TrendDataPoint {
  date: string;
  count: number;
  averageRating: number;
  positiveCount: number;
  negativeCount: number;
}

export interface FeedbackFilter {
  from?: string;
  to?: string;
  category?: FeedbackCategory;
  sentiment?: SentimentType;
  minRating?: number;
  maxRating?: number;
  region?: string;
  productId?: string;
  isResolved?: boolean;
  page?: number;
  pageSize?: number;
}

// ── Search (Elasticsearch) ────────────────────────────────────────────────

export interface SearchHit {
  item: FeedbackItem;
  score: number;
  highlight: string | null;
}

export interface SearchResultDto {
  query: string;
  totalHits: number;
  page: number;
  pageSize: number;
  hits: SearchHit[];
}

// ── Audit (MongoDB) ───────────────────────────────────────────────────────

export interface AuditLogEntry {
  id: string;
  action: string;
  feedbackId: number;
  timestamp: string;
  changeSummary: string | null;
  beforeJson: string | null;
  afterJson: string | null;
}
