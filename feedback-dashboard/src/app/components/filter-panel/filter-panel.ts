import {
  Component,
  EventEmitter,
  Input,
  OnInit,
  Output,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FeedbackFilter, SentimentType } from '../../models/feedback.models';
import { FeedbackService } from '../../services/feedback.service';

@Component({
  selector: 'app-filter-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './filter-panel.html',
  styleUrl: './filter-panel.scss',
})
export class FilterPanelComponent implements OnInit {
  /** Seed values applied once on init — NOT watched for changes after that. */
  @Input() initialFilter: FeedbackFilter = {};
  @Output() filterChange = new EventEmitter<FeedbackFilter>();

  private readonly svc = inject(FeedbackService);

  categories: string[] = [];
  regions: string[] = [];
  sentiments: SentimentType[] = ['Positive', 'Neutral', 'Negative'];

  // Working copy — owned entirely by this component; never overwritten by parent.
  filter: FeedbackFilter = {
    from: '',
    to: '',
    page: 1,
    pageSize: 15,
  };

  ngOnInit(): void {
    // Seed the working copy from the initial filter exactly once.
    this.filter = { ...this.initialFilter };
    // Load lookup lists once.
    this.svc.getCategories().subscribe(c => (this.categories = c));
    this.svc.getRegions().subscribe(r => (this.regions = r));
  }

  apply(): void {
    // Always emit page:1 so a new search starts from the beginning.
    this.filterChange.emit({ ...this.filter, page: 1 });
  }

  reset(): void {
    this.filter = { page: 1, pageSize: 15 };
    this.filterChange.emit({ ...this.filter });
  }
}
