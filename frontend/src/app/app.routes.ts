import { Routes } from '@angular/router';

// First routes this app has ever had - introduced for the usage
// dashboard (docs/log-dashboard.md section 6). '' stays the chat UI
// (now its own routed component, ChatPageComponent, extracted out of
// AppComponent) so existing bookmarks/the bare app URL keep working
// exactly as before.
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./components/chat-page/chat-page.component').then(m => m.ChatPageComponent),
  },
  {
    path: 'usage',
    loadComponent: () => import('./components/usage/usage-dashboard/usage-dashboard.component').then(m => m.UsageDashboardComponent),
  },
];
