import { Injectable, computed, signal } from '@angular/core';
import { ChatApiService } from './chat-api.service';
import { detectLanguage } from './language-detector';
import { parseCarSelection } from './selection-parser';
import { sortCarsForDisplay } from '../utils/car-sort';
import type { CarOption, CaseSummary, ChatMessage, ChatResponse } from '../models/chat.models';

function generateId(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

// Session state lives entirely server-side (ChatService.Models.Session),
// keyed by sessionId - this store only needs to hold one for the lifetime
// of the browser tab and replay it on every request, plus mirror the
// confirmed car so the UI can show it and the "confirm" action can attach
// confirmedCarId (+ codiceMotore/marca as a fallback) to the next request.
@Injectable({ providedIn: 'root' })
export class ChatStore {
  private readonly sessionId = crypto.randomUUID();

  // No manual switcher - detected from what the mechanic types, per
  // message. Short/ambiguous text (a bare DTC code, "si") often can't be
  // detected at all, so it falls back to whatever was last detected this
  // session rather than flapping back to the default.
  private language = 'it';

  readonly messages = signal<ChatMessage[]>([]);
  readonly confirmedCar = signal<CarOption | null>(null);
  readonly isStreaming = signal(false);
  readonly lastResponse = signal<ChatResponse | null>(null);
  readonly detectedLanguage = signal('it');

  // Flat car list in the same visual order CarSelectionListComponent renders:
  // groups sorted alphabetically (Altro last), within each group by annoInizio
  // desc then codiceMotore asc. Badge number N on a card = carDisplayOrder[N-1].
  readonly carDisplayOrder = computed<CarOption[]>(() =>
    sortCarsForDisplay(this.lastResponse()?.carMatches ?? []),
  );

  // The cases[] from the most recent assistant message that has ≥2 cases and
  // no case currently expanded. Non-null means the compact selection list is
  // visible and ready to receive a number reference (click, type, or voice).
  readonly pendingDocSelection = computed<CaseSummary[] | null>(() => {
    const msgs = this.messages();
    for (let i = msgs.length - 1; i >= 0; i--) {
      const m = msgs[i];
      if (m.role === 'assistant' && m.cases && m.cases.length >= 2 && m.selectedCaseIndex == null) {
        return m.cases;
      }
    }
    return null;
  });

  constructor(private readonly api: ChatApiService) {}

  async sendMessage(text: string): Promise<void> {
    if (!text.trim() || this.isStreaming()) return;

    // If a car-selection list is currently visible, try to parse the typed
    // text as a number reference before sending to the backend. A bare "8",
    // "otto", "the eighth", etc. should confirm the car at visual position 8
    // without creating a /api/chat/stream call with that literal text as the
    // body (which would fail TooVague validation or confuse Gemini).
    const displayOrder = this.carDisplayOrder();
    if (displayOrder.length > 0) {
      const index = parseCarSelection(text, this.language, displayOrder.length);
      if (index !== null) {
        const car = displayOrder[index];
        if (car) {
          if (this.pendingDocSelection()) {
            console.warn('[ChatStore] Both car and doc selection pending — car wins.');
          }
          await this.confirmCar(car);
          return;
        }
      }
    }

    // If a multi-doc compact list is visible, try to parse in strict mode so
    // that "ho 2 auto" (embedded number sentence) routes normally to the
    // backend, while "2" or "il secondo caso" triggers local expansion.
    const pendingDocs = this.pendingDocSelection();
    if (pendingDocs) {
      const index = parseCarSelection(text, this.language, pendingDocs.length, true);
      if (index !== null) {
        this.selectDocumentInLastResponse(index);
        return;
      }
    }

    this.language = detectLanguage(text, this.language);
    this.detectedLanguage.set(this.language);
    await this.send(text);
  }

  // Expands case at `index` in the message with `messageId`. Updates
  // selectedCaseIndex on that message and returns the CaseSummary, or null if
  // messageId/index is invalid.
  selectDocument(messageId: string, index: number): CaseSummary | null {
    let found: CaseSummary | null = null;
    this.messages.update(msgs =>
      msgs.map(m => {
        if (m.id === messageId && m.cases && index >= 0 && index < m.cases.length) {
          found = m.cases[index];
          return { ...m, selectedCaseIndex: index };
        }
        return m;
      }),
    );
    return found;
  }

  // Finds the most recent assistant message with ≥2 cases and no expanded
  // selection, then expands the case at `index`. Returns the CaseSummary or
  // null if no such message exists or index is out of range.
  selectDocumentInLastResponse(index: number): CaseSummary | null {
    const msgs = this.messages();
    for (let i = msgs.length - 1; i >= 0; i--) {
      const m = msgs[i];
      if (m.role === 'assistant' && m.cases && m.cases.length >= 2 && m.selectedCaseIndex == null) {
        return this.selectDocument(m.id, index);
      }
    }
    return null;
  }

  // Collapses an expanded document back to the compact list view.
  clearDocumentSelection(messageId: string): void {
    this.messages.update(msgs =>
      msgs.map(m => (m.id === messageId ? { ...m, selectedCaseIndex: null } : m)),
    );
  }

  async confirmCarByIndex(index: number): Promise<void> {
    const cars = this.lastResponse()?.carMatches;
    if (!cars || index < 0 || index >= cars.length) return;
    await this.confirmCar(cars[index]);
  }

  // Called when the mechanic picks a car from a carMatches list. Sends
  // its idMacchina as confirmedCarId - the only unambiguous identity, per
  // RepairOrchestrator.ConfirmCarAsync - plus codiceMotore/marca as a
  // fallback for the backend. Sending only codiceMotore/marca here was
  // the root cause of a real bug: several IVECO Daily III trims share one
  // engine code, so the backend had no way to tell which specific card
  // was clicked and silently resolved to an arbitrary one.
  //
  // The synthetic "Confermo il veicolo: ..." text is still sent to the
  // backend (Gemini needs it in History to correctly replay a pending
  // search per Rule 7 - see RepairOrchestrator), just no longer shown as
  // its own user bubble - paired with RepairOrchestrator no longer
  // yielding a hardcoded "Veicolo confermato: ..." reply either, since
  // the routing call that runs right after already produces its own
  // natural acknowledgment (or the replayed search result) from the same
  // synthetic fact. Two robotic confirmation bubbles before Gemini's real
  // reply was redundant, not a deliberate design.
  async confirmCar(car: CarOption): Promise<void> {
    if (this.isStreaming()) return;
    const label = `${car.marca} ${car.modello} ${car.motorizzazione ?? ''}`.trim();
    await this.send(`Confermo il veicolo: ${label}`, car, { showUserMessage: false });
  }

  reset(): void {
    this.messages.set([]);
    this.confirmedCar.set(null);
    this.lastResponse.set(null);
    // Best-effort: clear backend session so ghost history doesn't influence
    // Gemini routing on the next conversation (Rule 6).
    this.api.resetSession(this.sessionId).catch(() => {});
  }

  private async send(
    text: string,
    carBeingConfirmed?: CarOption,
    options?: { showUserMessage?: boolean },
  ): Promise<void> {
    this.isStreaming.set(true);

    if (options?.showUserMessage ?? true) {
      this.messages.update(msgs => [...msgs, { id: generateId(), role: 'user', text }]);
    }

    // One turn = one api.stream call, so all events in this callback belong
    // to the same assistant reply. M7: the first event of the turn creates the
    // bubble; every later event (once the stream actually yields more than
    // once - see H3) UPDATES that same bubble in place instead of appending a
    // new one. Without this, a two-yield turn (e.g. a "searching..."
    // confirmation followed by the results) stacked duplicate bubbles. The id
    // is generated frontend-side and is sufficient to key on; the backend
    // needs no turn identifier.
    let assistantMessageId: string | null = null;
    try {
      await this.api.stream(
        {
          sessionId: this.sessionId,
          message: text,
          confirmedCarId: carBeingConfirmed?.idMacchina,
          confirmedCodiceMotore: carBeingConfirmed?.codiceMotore,
          confirmedMarca: carBeingConfirmed?.marca,
          language: this.language,
        },
        (event: ChatResponse) => {
          this.lastResponse.set(event);
          if (carBeingConfirmed) {
            this.confirmedCar.set(carBeingConfirmed);
          }
          if (assistantMessageId === null) {
            assistantMessageId = generateId();
            const id = assistantMessageId;
            this.messages.update(msgs => [
              ...msgs,
              {
                id,
                role: 'assistant',
                text: event.message,
                carMatches: event.carMatches,
                cases: event.cases,
              },
            ]);
          } else {
            const id = assistantMessageId;
            this.messages.update(msgs =>
              msgs.map(m =>
                m.id === id
                  ? {
                      ...m,
                      text: event.message,
                      carMatches: event.carMatches,
                      cases: event.cases,
                      // A fresh final result for this turn is not expanded;
                      // drop any stale expansion from an earlier event.
                      selectedCaseIndex: null,
                    }
                  : m,
              ),
            );
          }
        },
      );
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Errore sconosciuto';
      this.messages.update(msgs => [
        ...msgs,
        { id: generateId(), role: 'assistant', text: `Errore: ${message}` },
      ]);
    } finally {
      this.isStreaming.set(false);
    }
  }
}
