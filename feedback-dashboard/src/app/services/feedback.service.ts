import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  FeedbackItem,
  FeedbackCreateDto,
  FeedbackUpdateDto,
  FeedbackFilter,
  FeedbackSummary,
  TrendDataPoint,
  PagedResult,
} from '../models/feedback.models';

@Injectable({ providedIn: 'root' })
export class FeedbackService {
  private readonly http = inject(HttpClient);
  private readonly base = 'http://localhost:5000/api/feedback';

  /** Build HttpParams from a FeedbackFilter object, omitting undefined values. */
  private toParams(filter: FeedbackFilter): HttpParams {
    let params = new HttpParams();
    Object.entries(filter).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });
    return params;
  }

  // ── CRUD ──────────────────────────────────────────────────────────────────

  getAll(filter: FeedbackFilter = {}): Observable<PagedResult<FeedbackItem>> {
    return this.http.get<PagedResult<FeedbackItem>>(this.base, {
      params: this.toParams(filter),
    });
  }

  getById(id: number): Observable<FeedbackItem> {
    return this.http.get<FeedbackItem>(`${this.base}/${id}`);
  }

  create(dto: FeedbackCreateDto): Observable<FeedbackItem> {
    return this.http.post<FeedbackItem>(this.base, dto);
  }

  update(id: number, dto: FeedbackUpdateDto): Observable<FeedbackItem> {
    return this.http.put<FeedbackItem>(`${this.base}/${id}`, dto);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  // ── Analytics ─────────────────────────────────────────────────────────────

  getSummary(filter: FeedbackFilter = {}): Observable<FeedbackSummary> {
    return this.http.get<FeedbackSummary>(`${this.base}/summary`, {
      params: this.toParams(filter),
    });
  }

  getTrends(
    from: string,
    to: string,
    groupBy: 'day' | 'week' | 'month' = 'day'
  ): Observable<TrendDataPoint[]> {
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('groupBy', groupBy);
    return this.http.get<TrendDataPoint[]>(`${this.base}/trends`, { params });
  }

  getCategories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/categories`);
  }

  getRegions(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/regions`);
  }
}
