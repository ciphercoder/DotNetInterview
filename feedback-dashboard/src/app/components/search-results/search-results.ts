import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SearchResultDto } from '../../models/feedback.models';

@Component({
  selector: 'app-search-results',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './search-results.html',
  styleUrl: './search-results.scss',
})
export class SearchResultsComponent {
  @Input() result: SearchResultDto | null = null;
  @Input() loading = false;
  @Output() pageChange = new EventEmitter<number>();

  get pages(): number[] {
    if (!this.result) return [];
    const total = Math.ceil(this.result.totalHits / this.result.pageSize);
    return Array.from({ length: total }, (_, i) => i + 1);
  }

  sentimentClass(s: string): string {
    return s.toLowerCase();
  }

  stars(r: number): string {
    return '★'.repeat(r) + '☆'.repeat(5 - r);
  }
}
