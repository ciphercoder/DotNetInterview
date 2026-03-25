import {
  Component,
  EventEmitter,
  Input,
  Output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FeedbackItem, PagedResult } from '../../models/feedback.models';

@Component({
  selector: 'app-feedback-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './feedback-list.html',
  styleUrl: './feedback-list.scss',
})
export class FeedbackListComponent {
  @Input() result: PagedResult<FeedbackItem> | null = null;
  @Input() loading = false;
  @Output() pageChange = new EventEmitter<number>();
  @Output() resolveToggle = new EventEmitter<FeedbackItem>();
  @Output() viewAudit = new EventEmitter<FeedbackItem>();

  get pages(): number[] {
    if (!this.result) return [];
    return Array.from({ length: this.result.totalPages }, (_, i) => i + 1);
  }

  sentimentClass(s: string): string {
    return s.toLowerCase();
  }

  stars(rating: number): string {
    return '★'.repeat(rating) + '☆'.repeat(5 - rating);
  }
}
