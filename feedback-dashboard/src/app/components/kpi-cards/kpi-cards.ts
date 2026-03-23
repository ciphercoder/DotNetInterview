import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FeedbackSummary } from '../../models/feedback.models';

@Component({
  selector: 'app-kpi-cards',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './kpi-cards.html',
  styleUrl: './kpi-cards.scss',
})
export class KpiCardsComponent {
  @Input() summary: FeedbackSummary | null = null;

  get sentimentPct(): { pos: number; neu: number; neg: number } {
    if (!this.summary || this.summary.totalCount === 0)
      return { pos: 0, neu: 0, neg: 0 };
    const t = this.summary.totalCount;
    return {
      pos: Math.round((this.summary.positiveCount / t) * 100),
      neu: Math.round((this.summary.neutralCount / t) * 100),
      neg: Math.round((this.summary.negativeCount / t) * 100),
    };
  }
}
