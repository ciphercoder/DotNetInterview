import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FeedbackFilter, FeedbackCategory, SentimentType } from '../../models/feedback.models';
import { FeedbackService } from '../../services/feedback.service';

@Component({
  selector: 'app-filter-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './filter-panel.html',
  styleUrl: './filter-panel.scss',
})
export class FilterPanelComponent implements OnChanges {
  @Input() initialFilter: FeedbackFilter = {};
  @Output() filterChange = new EventEmitter<FeedbackFilter>();

  private readonly svc = inject(FeedbackService);

  categories: string[] = [];
  regions: string[] = [];
  sentiments: SentimentType[] = ['Positive', 'Neutral', 'Negative'];

  filter: FeedbackFilter = {
    from: '',
    to: '',
    page: 1,
    pageSize: 20,
  };

  ngOnChanges(): void {
    this.filter = { ...this.initialFilter };
    this.svc.getCategories().subscribe(c => (this.categories = c));
    this.svc.getRegions().subscribe(r => (this.regions = r));
  }

  apply(): void {
    this.filterChange.emit({ ...this.filter, page: 1 });
  }

  reset(): void {
    this.filter = { page: 1, pageSize: 20 };
    this.filterChange.emit({ ...this.filter });
  }
}
