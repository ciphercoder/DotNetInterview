import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration, ChartData } from 'chart.js';
import {
  Chart,
  DoughnutController,
  ArcElement,
  Tooltip,
  Legend,
} from 'chart.js';
import { FeedbackSummary } from '../../models/feedback.models';

Chart.register(DoughnutController, ArcElement, Tooltip, Legend);

@Component({
  selector: 'app-sentiment-chart',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
  templateUrl: './sentiment-chart.html',
  styleUrl: './sentiment-chart.scss',
})
export class SentimentChartComponent implements OnChanges {
  @Input() summary: FeedbackSummary | null = null;

  chartData: ChartData<'doughnut'> = {
    labels: ['Positive', 'Neutral', 'Negative'],
    datasets: [{ data: [0, 0, 0] }],
  };

  chartOptions: ChartConfiguration<'doughnut'>['options'] = {
    responsive: true,
    maintainAspectRatio: false,
    cutout: '70%',
    plugins: {
      legend: { position: 'bottom' },
      tooltip: {
        callbacks: {
          label: ctx => ` ${ctx.label}: ${ctx.parsed} (${
            ctx.dataset.data.reduce((a: number, b) => a + (b as number), 0)
              ? Math.round((ctx.parsed / (ctx.dataset.data.reduce((a: number, b) => a + (b as number), 0) as number)) * 100)
              : 0
          }%)`,
        },
      },
    },
  };

  ngOnChanges(): void {
    if (!this.summary) return;
    this.chartData = {
      labels: ['Positive', 'Neutral', 'Negative'],
      datasets: [
        {
          data: [
            this.summary.positiveCount,
            this.summary.neutralCount,
            this.summary.negativeCount,
          ],
          backgroundColor: ['#22c55e', '#f59e0b', '#ef4444'],
          hoverOffset: 6,
        },
      ],
    };
  }
}
