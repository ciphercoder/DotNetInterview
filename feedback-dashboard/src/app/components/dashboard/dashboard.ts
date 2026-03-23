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
} from '../../models/feedback.models';

import { FilterPanelComponent } from '../filter-panel/filter-panel';
import { KpiCardsComponent } from '../kpi-cards/kpi-cards';
import { TrendChartComponent } from '../trend-chart/trend-chart';
import { SentimentChartComponent } from '../sentiment-chart/sentiment-chart';
import { CategoryChartComponent } from '../category-chart/category-chart';
import { FeedbackListComponent } from '../feedback-list/feedback-list';

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
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent implements OnInit {
  private readonly svc = inject(FeedbackService);

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

  ngOnInit(): void {
    this.loadAll();
  }

  onFilterChange(f: FeedbackFilter): void {
    this.filter = f;
    this.loadAll();
  }

  onPageChange(page: number): void {
    this.filter = { ...this.filter, page };
    this.loadList();
  }

  onResolveToggle(item: FeedbackItem): void {
    this.svc.update(item.id, { isResolved: !item.isResolved }).subscribe(() => {
      this.loadAll();
    });
  }

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
