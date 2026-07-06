import { Injectable } from '@angular/core';
import type { ChatRequest, ChatResponse } from '../models/chat.models';

// Relative path: nginx proxies /api/chat/* to chat-service in prod;
// `ng serve`'s dev proxy (proxy.conf.json) forwards the same path locally.
const STREAM_URL = '/api/chat/stream';
const TRANSCRIBE_URL = '/api/chat/transcribe';

@Injectable({ providedIn: 'root' })
export class ChatApiService {
  // POST /api/chat/stream returns SSE where each "data:" line is one
  // complete ChatResponse JSON object (not a token stream, no [DONE]
  // sentinel - see services/chat/Controllers/ChatController.cs). Angular's
  // HttpClient has no SSE support, so this uses fetch + ReadableStream
  // directly and calls onEvent for each parsed ChatResponse as it arrives.
  async stream(request: ChatRequest, onEvent: (response: ChatResponse) => void): Promise<void> {
    const res = await fetch(STREAM_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    if (!res.ok || !res.body) {
      throw new Error(`Chat stream failed: ${res.status} ${res.statusText}`);
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let lineBuffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      lineBuffer += decoder.decode(value, { stream: true });
      const lines = lineBuffer.split('\n');
      lineBuffer = lines.pop() ?? '';

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue;
        const data = line.slice('data: '.length).trim();
        if (!data) continue;
        onEvent(JSON.parse(data) as ChatResponse);
      }
    }
  }

  async transcribe(audio: Blob): Promise<string> {
    const formData = new FormData();
    formData.append('audio', audio, 'recording.webm');

    const res = await fetch(TRANSCRIBE_URL, { method: 'POST', body: formData });
    if (!res.ok) {
      throw new Error(`Transcribe failed: ${res.status} ${res.statusText}`);
    }
    return res.text();
  }
}
