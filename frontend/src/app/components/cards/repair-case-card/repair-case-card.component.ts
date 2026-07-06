import { Component, Input } from '@angular/core';
import { StarRatingComponent } from '../../ui/star-rating/star-rating.component';
import { DtcBadgeComponent } from '../../ui/dtc-badge/dtc-badge.component';
import type { CaseSummary } from '../../../models/chat.models';

// Verbatim from the real source .resx files (Data/resx_samples, XCAPITOLO
// Ordine=1's Corpo) - identical boilerplate across every document in a
// given language, only the star count in the Capitolo title varies per
// document. resx_parser.py only keeps that star count (-> reliability);
// the explanatory legend text itself was otherwise discarded entirely.
// Hardcoded here rather than re-ingested into the documents table since
// it's universal system text, not per-document data - same category as
// RepairOrchestrator's other hardcoded per-language static text (Rule 9
// clarification questions, FindCar not-found messages), not the
// fidelity-from-a-real-query rule that applies to document content.
const RELIABILITY_LEGEND: Record<string, string> = {
  it: `* livello basso: Rilevazione dell'autoriparatore, casistica del guasto non riscontrata dalla Casa Costruttrice.
** livello medio: Casistica ampia del guasto, riscontrata dai tecnici di assistenza e dagli autoriparatori.
*** livello alto: Certezza dell'anomalia, buon grado di "ripetitività" segnalata dalla Casa Costruttrice.`,
  en: `* low level: Detection of the car repairer, case study of the fault not found by the manufacturer.
** medium level: Extensive fault history, found by service technicians and repairers.
*** high level: Certainty of the fault, good degree of "repetition" reported by the manufacturer.`,
  fr: `* niveau faible : constatation du réparateur automobile, cas de panne non constaté par le constructeur.
** niveau moyen : nombreux cas de panne constatés par les techniciens d'assistance et les réparateurs automobiles.
*** niveau élevé : certitude de l'anomalie, bon degré de « répétitivité » signalé par le constructeur.`,
  es: `* nivel bajo: Detección del mecánico, caso de avería no detectado por el fabricante.
** nivel medio: Casos frecuentes de avería, detectados por los técnicos de asistencia y los mecánicos.
*** nivel alto: Anomalía confirmada, buen grado de «repetitividad» señalado por el fabricante.`,
  pt: `* nível baixo: Detecção pelo mecânico, caso de avaria não constatado pelo fabricante.
** nível médio: Casos frequentes da avaria, constatados pelos técnicos de assistência e pelos mecânicos.
*** nível alto: Certeza da anomalia, bom grau de «repetitividade» assinalado pelo fabricante.`,
};

// Section headings and field labels, taken verbatim from the real per-language
// .resx source files (Data/resx_samples - XCAPITOLO's own <Capitolo> chapter
// titles for Ordine 1/3/4, and the <B>label:</B> markers inside each chapter's
// Corpo - see resx_parser.py's positional-extraction comments for why these are
// always in the same order across languages). Not invented translations - same
// fidelity principle as RELIABILITY_LEGEND above. Previously this whole card
// rendered these labels in Italian regardless of caseSummary.language, while
// the legend text alone was already correctly localized - a real, confirmed
// bug (a Portuguese document showed Italian "Impianto:"/"Procedura di
// riparazione" labels around its own Portuguese content).
interface CardLabels {
  reliabilityHeading: string;
  identificationHeading: string;
  impianto: string;
  dispositivo: string;
  anomalia: string;
  dtcIntro: string;
  causa: string;
  repairHeading: string;
  intervento: string;
  procedura: string;
  nota: string;
}

const LABELS: Record<string, CardLabels> = {
  it: {
    reliabilityHeading: 'Grado di attendibilità (*)',
    identificationHeading: 'Identificazione del sistema / guasto',
    impianto: 'Impianto:',
    dispositivo: 'Dispositivo:',
    anomalia: 'Anomalia:',
    dtcIntro: "Errori rilevati dall'autodiagnosi:",
    causa: 'Causa:',
    repairHeading: 'Procedura di riparazione',
    intervento: 'Intervento',
    procedura: 'Procedura',
    nota: 'Nota',
  },
  en: {
    reliabilityHeading: 'Level of reliability (*)',
    identificationHeading: 'System/fault identification',
    impianto: 'System:',
    dispositivo: 'Device:',
    anomalia: 'Fault:',
    dtcIntro: 'Errors detected by self-diagnosis:',
    causa: 'Cause:',
    repairHeading: 'Repair procedure',
    intervento: 'Action',
    procedura: 'Procedure',
    nota: 'Note',
  },
  fr: {
    reliabilityHeading: 'Degré de fiabilité (*)',
    identificationHeading: 'Identification du système / de la panne',
    impianto: 'Système :',
    dispositivo: 'Dispositif :',
    anomalia: 'Anomalie :',
    dtcIntro: "Erreurs détectées par l'autodiagnostic :",
    causa: 'Cause :',
    repairHeading: 'Procédure de réparation',
    intervento: 'Intervention',
    procedura: 'Procédure',
    nota: 'Remarque',
  },
  pt: {
    reliabilityHeading: 'Grau de fiabilidade (*)',
    identificationHeading: 'Identificação do sistema / avaria',
    impianto: 'Sistema:',
    dispositivo: 'Dispositivo:',
    anomalia: 'Anomalia:',
    dtcIntro: 'Erros detetados pelo autodiagnóstico:',
    causa: 'Causa:',
    repairHeading: 'Procedimento de reparação',
    intervento: 'Intervenção',
    procedura: 'Procedimento',
    nota: 'Nota',
  },
  es: {
    reliabilityHeading: 'Grado de fiabilidad (*)',
    identificationHeading: 'Identificación del sistema / avería',
    impianto: 'Sistema:',
    dispositivo: 'Dispositivo:',
    anomalia: 'Anomalía:',
    dtcIntro: 'Errores detectados por el autodiagnóstico:',
    causa: 'Causa:',
    repairHeading: 'Procedimiento de reparación',
    intervento: 'Intervención',
    procedura: 'Procedimiento',
    nota: 'Nota',
  },
};

@Component({
  selector: 'app-repair-case-card',
  standalone: true,
  imports: [StarRatingComponent, DtcBadgeComponent],
  template: `
    <div class="case-card bg-surface border-border">
      @if (caseSummary.titolo) {
        <div class="titolo text-foreground">{{ caseSummary.titolo }}</div>
      }

      <div class="case-header">
        <span class="sigla text-accent">{{ caseSummary.sigla }}</span>
        <app-star-rating [reliability]="caseSummary.reliability" />
      </div>

      <div class="section-heading text-muted">{{ labels().reliabilityHeading }}</div>
      <div class="case-text text-muted legend-text">{{ legend() }}</div>

      <div class="section-heading text-muted">{{ labels().identificationHeading }}</div>
      <div class="case-row text-foreground"><span class="label text-muted">{{ labels().impianto }}</span> {{ caseSummary.impianto }}</div>
      <div class="case-row text-foreground"><span class="label text-muted">{{ labels().dispositivo }}</span> {{ caseSummary.dispositivo }}</div>
      <div class="case-row text-foreground"><span class="label text-muted">{{ labels().anomalia }}</span> {{ caseSummary.anomalia }}</div>
      @if (caseSummary.dtcCodes.length > 0) {
        <div class="case-block dtc-block">
          <div class="label text-muted">{{ labels().dtcIntro }}</div>
          @for (dtc of caseSummary.dtcCodes; track dtc.code) {
            <div class="dtc-line">
              <app-dtc-badge [code]="dtc.code" />
              @if (dtc.description) {
                <span class="dtc-description text-foreground">{{ dtc.description }}</span>
              }
            </div>
          }
        </div>
      }
      <div class="case-row text-foreground"><span class="label text-muted">{{ labels().causa }}</span> {{ caseSummary.causa }}</div>

      <div class="section-heading text-muted">{{ labels().repairHeading }}</div>
      <div class="case-block">
        <div class="label text-muted">{{ labels().intervento }}</div>
        <div class="case-text text-foreground">{{ caseSummary.intervento }}</div>
      </div>

      @if (caseSummary.procedura && caseSummary.procedura !== '- -') {
        <div class="case-block">
          <div class="label text-muted">{{ labels().procedura }}</div>
          <div class="case-text text-foreground">{{ caseSummary.procedura }}</div>
        </div>
      }

      @if (caseSummary.nota && caseSummary.nota !== '- -') {
        <div class="case-block">
          <div class="label text-muted">{{ labels().nota }}</div>
          <div class="case-text text-foreground">{{ caseSummary.nota }}</div>
        </div>
      }
    </div>
  `,
  styleUrl: './repair-case-card.component.css',
})
export class RepairCaseCardComponent {
  @Input({ required: true }) caseSummary!: CaseSummary;

  legend(): string {
    return RELIABILITY_LEGEND[this.caseSummary.language] ?? RELIABILITY_LEGEND['it'];
  }

  labels(): CardLabels {
    return LABELS[this.caseSummary.language] ?? LABELS['it'];
  }
}
