import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartConfiguration } from 'chart.js';
import {
  Chart,
  BarController,
  BarElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
} from 'chart.js';
import { FeedbackSummary } from '../../models/feedback.models';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend);

@Component({
  selector: 'app-category-chart',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './category-chart.html',
  styleUrl: './category-chart.scss',
})
export class CategoryChartComponent implements OnChanges {
  @Input() summary: FeedbackSummary | null = null;

  chartData: ChartData<'bar'> = { labels: [], datasets: [] };

  chartOptions: ChartConfiguration<'bar'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
    },
    scales: {
      x: { grid: { display: false } },
      y: { beginAtZero: true, ticks: { precision: 0 } },
    },
  };

  ngOnChanges(): void {
    if (!this.summary) return;
    const entries = Object.entries(this.summary.countByCategory);
    this.chartData = {
      labels: entries.map(([k]) => k),
      datasets: [
        {
          label: 'Count',
          data: entries.map(([, v]) => v),
          backgroundColor: '#6366f1',
          borderRadius: 6,
        },
      ],
    };
  }
}
