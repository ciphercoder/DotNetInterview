import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuditLogEntry } from '../../models/feedback.models';

@Component({
  selector: 'app-audit-log',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './audit-log.html',
  styleUrl: './audit-log.scss',
})
export class AuditLogComponent {
  @Input() logs: AuditLogEntry[] = [];
  @Input() loading = false;

  expandedId: string | null = null;

  toggle(id: string): void {
    this.expandedId = this.expandedId === id ? null : id;
  }

  actionClass(action: string): string {
    switch (action.toLowerCase()) {
      case 'create': return 'create';
      case 'update': return 'update';
      case 'delete': return 'delete';
      default:       return '';
    }
  }

  formatJson(raw: string | null): string {
    if (!raw) return '';
    try { return JSON.stringify(JSON.parse(raw), null, 2); }
    catch { return raw; }
  }
}
