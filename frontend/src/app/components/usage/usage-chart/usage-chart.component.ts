import { Component, Input } from '@angular/core';
import type { UsageTimeSeriesPoint } from '../../../models/usage.models';

interface Bar {
  x: number;
  y: number;
  width: number;
  height: number;
  label: string;
  title: string;
}

// Hand-built SVG bar chart - no charting library exists in this app yet,
// and an internal usage dashboard at this project's scale doesn't need
// one (docs/log-dashboard.md section 6). Plots cost in USD per bucket;
// richer interactivity (zoom, hover tooltips beyond the plain <title>
// here) is a v2 problem, not a prerequisite.
@Component({
  selector: 'app-usage-chart',
  standalone: true,
  template: `
    <svg [attr.viewBox]="'0 0 ' + viewWidth + ' ' + viewHeight" class="usage-chart" preserveAspectRatio="none">
      @for (bar of bars(); track bar.label) {
        <rect [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width" [attr.height]="bar.height" class="bar">
          <title>{{ bar.title }}</title>
        </rect>
        <text [attr.x]="bar.x + bar.width / 2" [attr.y]="viewHeight - 4" class="bar-label" text-anchor="middle">{{ bar.label }}</text>
      }
      @if (points.length === 0) {
        <text [attr.x]="viewWidth / 2" [attr.y]="viewHeight / 2" class="empty-label" text-anchor="middle">Nessun dato</text>
      }
    </svg>
  `,
  styleUrl: './usage-chart.component.css',
})
export class UsageChartComponent {
  @Input({ required: true }) points: UsageTimeSeriesPoint[] = [];

  readonly viewWidth = 600;
  readonly viewHeight = 200;
  private readonly bottomPadding = 20;
  private readonly topPadding = 10;
  private readonly gap = 4;

  bars(): Bar[] {
    if (this.points.length === 0) return [];

    const maxCost = Math.max(...this.points.map(p => p.costUsd), 0.000001);
    const plotHeight = this.viewHeight - this.bottomPadding - this.topPadding;
    const barWidth = this.viewWidth / this.points.length - this.gap;

    return this.points.map((point, i) => {
      const height = (point.costUsd / maxCost) * plotHeight;
      return {
        x: i * (barWidth + this.gap) + this.gap / 2,
        y: this.viewHeight - this.bottomPadding - height,
        width: Math.max(barWidth, 1),
        height: Math.max(height, 1),
        label: this.formatLabel(point.bucketStart),
        title: `${this.formatLabel(point.bucketStart)}: $${point.costUsd.toFixed(6)} (${point.tokens} token, ${point.calls} chiamate)`,
      };
    });
  }

  private formatLabel(bucketStart: string): string {
    const date = new Date(bucketStart);
    return date.toLocaleDateString('it-IT', { day: '2-digit', month: '2-digit' })
      + (this.hasHourGranularity() ? ` ${date.getHours()}:00` : '');
  }

  private hasHourGranularity(): boolean {
    if (this.points.length < 2) return false;
    const a = new Date(this.points[0].bucketStart);
    const b = new Date(this.points[1].bucketStart);
    return Math.abs(b.getTime() - a.getTime()) < 24 * 60 * 60 * 1000;
  }
}
