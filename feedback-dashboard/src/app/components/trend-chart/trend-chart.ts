import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';
import {
  Chart,
  LineElement,
  PointElement,
  LineController,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  Filler,
} from 'chart.js';
import { TrendDataPoint } from '../../models/feedback.models';

Chart.register(
  LineElement, PointElement, LineController,
  CategoryScale, LinearScale, Tooltip, Legend, Filler
);

@Component({
  selector: 'app-trend-chart',
  standalone: true,
  imports: [CommonModule, FormsModule, BaseChartDirective],
  templateUrl: './trend-chart.html',
  styleUrl: './trend-chart.scss',
})
export class TrendChartComponent implements OnChanges {
  @Input() trendData: TrendDataPoint[] = [];
  @Input() groupBy: 'day' | 'week' | 'month' = 'day';
  @Output() groupByChange = new EventEmitter<'day' | 'week' | 'month'>();

  chartData: ChartData<'line'> = { labels: [], datasets: [] };

  chartOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: { mode: 'index', intersect: false },
    plugins: {
      legend: { position: 'top' },
      tooltip: { mode: 'index' },
    },
    scales: {
      x: {
        grid: { color: 'rgba(0,0,0,0.05)' },
        ticks: { maxTicksLimit: 12 },
      },
      y: {
        grid: { color: 'rgba(0,0,0,0.05)' },
        beginAtZero: true,
        title: { display: true, text: 'Feedback Count' },
      },
      yRating: {
        position: 'right',
        min: 0,
        max: 5,
        grid: { drawOnChartArea: false },
        title: { display: true, text: 'Avg Rating' },
      },
    },
  };

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['trendData']) {
      this.buildChart();
    }
  }

  buildChart(): void {
    const labels = this.trendData.map(d => d.date);
    this.chartData = {
      labels,
      datasets: [
        {
          label: 'Total',
          data: this.trendData.map(d => d.count),
          borderColor: '#6366f1',
          backgroundColor: 'rgba(99,102,241,.15)',
          fill: true,
          tension: 0.4,
          yAxisID: 'y',
        },
        {
          label: 'Positive',
          data: this.trendData.map(d => d.positiveCount),
          borderColor: '#22c55e',
          backgroundColor: 'transparent',
          tension: 0.4,
          yAxisID: 'y',
        },
        {
          label: 'Negative',
          data: this.trendData.map(d => d.negativeCount),
          borderColor: '#ef4444',
          backgroundColor: 'transparent',
          tension: 0.4,
          yAxisID: 'y',
        },
        {
          label: 'Avg Rating',
          data: this.trendData.map(d => d.averageRating),
          borderColor: '#f59e0b',
          backgroundColor: 'transparent',
          borderDash: [5, 3],
          tension: 0.4,
          yAxisID: 'yRating',
        },
      ],
    };
  }
}
