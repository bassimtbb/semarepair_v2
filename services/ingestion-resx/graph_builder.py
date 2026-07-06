"""
Builds the 7 graph edge types from parsed resx documents and the existing
gup_rows table (vehicle <-> document mapping, loaded by services/ingestion).
See docs/SemaRepair_Architecture.md section 5.8.

Each edge is a (from_type, from_id, relation, to_type, to_id, language,
description) tuple, matching the graph_edges columns (id/weight/created_at
have defaults). description is only ever non-None for CONTAINS_FAULT
edges - the DTC code's own explanation, taken verbatim from that specific
document's resx text (see resx_parser.py's fault_code_descriptions);
every other edge type always passes None for it.
"""

from collections import defaultdict
from itertools import combinations


def build_vehicle_edges(gup_rows: list[dict]) -> list[tuple]:
    """(a) Car -DOCUMENTED_IN-> Document, (g) Car -SHARES_ENGINE_WITH-> Car."""
    edges = []

    for row in gup_rows:
        edges.append(('car', row['id_macchina'], 'DOCUMENTED_IN', 'document', row['id_documento'], None, None))

    by_engine = defaultdict(set)
    for row in gup_rows:
        by_engine[row['codice_motore_macchina']].add(row['id_macchina'])

    for car_ids in by_engine.values():
        if len(car_ids) < 2:
            continue
        for car_a, car_b in combinations(sorted(car_ids), 2):
            edges.append(('car', car_a, 'SHARES_ENGINE_WITH', 'car', car_b, None, None))
            edges.append(('car', car_b, 'SHARES_ENGINE_WITH', 'car', car_a, None, None))

    return edges


def build_document_edges(resx_records: list[dict]) -> list[tuple]:
    """(b) CONTAINS_FAULT, (c) RELATED_TO, (d) AFFECTS_SYSTEM, (e) INVOLVES_DEVICE."""
    edges = []

    for r in resx_records:
        id_documento = r['id_documento']
        language = r['language']
        fault_codes = r['fault_codes']
        descriptions = r['fault_code_descriptions']

        for code in fault_codes:
            edges.append(('document', id_documento, 'CONTAINS_FAULT', 'faultcode', code, language, descriptions.get(code)))

        for code_a, code_b in combinations(fault_codes, 2):
            edges.append(('faultcode', code_a, 'RELATED_TO', 'faultcode', code_b, language, None))

        if r['impianto']:
            edges.append(('document', id_documento, 'AFFECTS_SYSTEM', 'system', r['impianto'], language, None))

        if r['dispositivo']:
            edges.append(('document', id_documento, 'INVOLVES_DEVICE', 'device', r['dispositivo'], language, None))

    return edges


def build_translation_edges(resx_records: list[dict], canonical_language: str = 'it') -> list[tuple]:
    """(f) Document -HAS_TRANSLATION-> Document, from the canonical language
    version to every other language version of the same document."""
    edges = []
    languages_by_doc = defaultdict(set)
    for r in resx_records:
        languages_by_doc[r['id_documento']].add(r['language'])

    for id_documento, languages in languages_by_doc.items():
        if canonical_language not in languages:
            continue
        from_id = f"{id_documento}_{canonical_language}"
        for lang in sorted(languages - {canonical_language}):
            edges.append(('document', from_id, 'HAS_TRANSLATION', 'document', f"{id_documento}_{lang}", None, None))

    return edges


def build_all_edges(resx_records: list[dict], gup_rows: list[dict]) -> list[tuple]:
    return (
        build_vehicle_edges(gup_rows)
        + build_document_edges(resx_records)
        + build_translation_edges(resx_records)
    )
