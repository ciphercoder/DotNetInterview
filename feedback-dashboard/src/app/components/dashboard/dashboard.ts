import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';

import { FeedbackService } from '../../services/feedback.service';
import {
  FeedbackFilter,
  FeedbackItem,
  FeedbackSummary,
  TrendDataPoint,
  PagedResult,
  SearchResultDto,
  AuditLogEntry,
} from '../../models/feedback.models';

import { FilterPanelComponent } from '../filter-panel/filter-panel';
import { KpiCardsComponent } from '../kpi-cards/kpi-cards';
import { TrendChartComponent } from '../trend-chart/trend-chart';
import { SentimentChartComponent } from '../sentiment-chart/sentiment-chart';
import { CategoryChartComponent } from '../category-chart/category-chart';
import { FeedbackListComponent } from '../feedback-list/feedback-list';
import { SearchBarComponent } from '../search-bar/search-bar';
import { SearchResultsComponent } from '../search-results/search-results';
import { AuditLogComponent } from '../audit-log/audit-log';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    FilterPanelComponent,
    KpiCardsComponent,
    TrendChartComponent,
    SentimentChartComponent,
    CategoryChartComponent,
    FeedbackListComponent,
    SearchBarComponent,
    SearchResultsComponent,
    AuditLogComponent,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent implements OnInit {
  private readonly svc = inject(FeedbackService);

  activeTab: 'dashboard' | 'search' = 'dashboard';

  filter: FeedbackFilter = {
    page: 1,
    pageSize: 15,
    from: this.daysAgo(30),
    to: this.today(),
  };

  summary: FeedbackSummary | null = null;
  trendData: TrendDataPoint[] = [];
  listResult: PagedResult<FeedbackItem> | null = null;
  listLoading = false;
  trendGroupBy: 'day' | 'week' | 'month' = 'day';

  // Search state
  searchResult: SearchResultDto | null = null;
  searchLoading = false;
  currentQuery = '';
  currentSearchPage = 1;

  // Audit state
  auditLogs: AuditLogEntry[] = [];
  auditLoading = false;
  selectedAuditItem: FeedbackItem | null = null;
  showAuditPanel = false;

  ngOnInit(): void {
    this.loadAll();
  }

  // ── Tab navigation ─────────────────────────────────────────────────────────

  setTab(tab: 'dashboard' | 'search'): void {
    this.activeTab = tab;
  }

  // ── Dashboard handlers ─────────────────────────────────────────────────────

  onFilterChange(f: FeedbackFilter): void {
    // Store the latest filter (including page:1 reset from the panel's apply()).
    this.filter = f;
    this.loadAll();
  }

  onPageChange(page: number): void {
    // Update only the page on the stored filter — do NOT reassign this.filter
    // to a new object, because that would flow into [initialFilter] and cause
    // FilterPanelComponent to re-init and lose the user's dropdown selections.
    this.filter.page = page;
    this.loadList();
  }

  onResolveToggle(item: FeedbackItem): void {
    this.svc.update(item.id, { isResolved: !item.isResolved }).subscribe(() => {
      this.loadAll();
    });
  }

  // ── Audit handlers ─────────────────────────────────────────────────────────

  onViewAudit(item: FeedbackItem): void {
    this.selectedAuditItem = item;
    this.showAuditPanel = true;
    this.auditLogs = [];
    this.auditLoading = true;
    this.svc.getAuditLog(item.id).subscribe({
      next: logs => {
        this.auditLogs = logs;
        this.auditLoading = false;
      },
      error: () => (this.auditLoading = false),
    });
  }

  closeAuditPanel(): void {
    this.showAuditPanel = false;
    this.selectedAuditItem = null;
    this.auditLogs = [];
  }

  // ── Search handlers ────────────────────────────────────────────────────────

  onSearch(query: string): void {
    this.currentQuery = query;
    this.currentSearchPage = 1;
    if (!query) {
      this.searchResult = null;
      return;
    }
    this.doSearch(query, 1);
  }

  onSearchPage(page: number): void {
    this.currentSearchPage = page;
    this.doSearch(this.currentQuery, page);
  }

  private doSearch(query: string, page: number): void {
    this.searchLoading = true;
    this.svc.search(query, page).subscribe({
      next: result => {
        this.searchResult = result;
        this.searchLoading = false;
      },
      error: () => (this.searchLoading = false),
    });
  }

  // ── Data loading ───────────────────────────────────────────────────────────

  private loadAll(): void {
    this.listLoading = true;

    forkJoin({
      summary: this.svc.getSummary(this.filter),
      trends: this.svc.getTrends(
        this.filter.from ?? this.daysAgo(30),
        this.filter.to ?? this.today(),
        this.trendGroupBy
      ),
      list: this.svc.getAll(this.filter),
    }).subscribe({
      next: ({ summary, trends, list }) => {
        this.summary = summary;
        this.trendData = trends;
        this.listResult = list;
        this.listLoading = false;
      },
      error: () => {
        this.listLoading = false;
      },
    });
  }

  private loadList(): void {
    this.listLoading = true;
    this.svc.getAll(this.filter).subscribe({
      next: list => {
        this.listResult = list;
        this.listLoading = false;
      },
      error: () => (this.listLoading = false),
    });
  }

  private today(): string {
    return new Date().toISOString().split('T')[0];
  }

  private daysAgo(n: number): string {
    const d = new Date();
    d.setDate(d.getDate() - n);
    return d.toISOString().split('T')[0];
  }
}
