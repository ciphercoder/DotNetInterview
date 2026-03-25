import {
  Component,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs/operators';

@Component({
  selector: 'app-search-bar',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './search-bar.html',
  styleUrl: './search-bar.scss',
})
export class SearchBarComponent implements OnInit, OnDestroy {
  @Output() searchQuery = new EventEmitter<string>();

  query = '';
  private readonly input$ = new Subject<string>();

  ngOnInit(): void {
    this.input$.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      filter(q => q.trim().length > 1 || q.trim().length === 0),
    ).subscribe(q => this.searchQuery.emit(q.trim()));
  }

  ngOnDestroy(): void {
    this.input$.complete();
  }

  onInput(value: string): void {
    this.input$.next(value);
  }

  clear(): void {
    this.query = '';
    this.input$.next('');
  }
}
