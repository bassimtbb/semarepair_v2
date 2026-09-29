# Démo SemaRepair — scénario vocal

Six phrases, dans l'ordre. Chaque bloc est un scénario complet : **ne mélange pas les
langues dans une même session.**

---

## 🇮🇹 Italiano

```
Sto lavorando su un Fiat Ducato 2.8 JTD del 2004
```
```
Il motore non si avvia dopo un arresto in marcia
```
```
Dove si trova il fusibile F17 e che amperaggio ha?
```
```
Mostrami lo schema elettrico dell'airbag
```
```
Ingrandisci lo schema
```
```
Come azzero l'indicatore
```

---

## 🇬🇧 English

```
I am working on a 2004 Fiat Ducato 2.8 JTD
```
```
The engine will not start after stalling while driving
```
```
Where is fuse F17 and what amperage is it?
```
```
Show me the airbag wiring diagram
```
```
Enlarge the diagram
```
```
How do I reset the service indicator
```

---

## 🇫🇷 Français

```
Je travaille sur un Fiat Ducato 2.8 JTD de 2004
```
```
Le moteur ne démarre plus après un arrêt en roulant
```
```
Où se trouve le fusible F17 et quel ampérage a-t-il ?
```
```
Montre-moi le schéma électrique de l'airbag
```
```
Agrandis le schéma
```
```
Comment remettre à zéro l'indicateur d'entretien
```

---

## Avant de commencer

- Clique la **carte du véhicule** après la phrase 1 — sans ça, rien d'autre ne marche.
- **Chrome**, pas Safari : le schéma est un cadre PDF.
- La phrase 5 n'agit que si le schéma de la phrase 4 est **encore à l'écran**.

## ⚠️ En français, saute les phrases 4 et 5

Les **126 légendes de schémas n'existent qu'en italien et en anglais**. En français la
phrase 4 ne rend pas le dessin — elle rend du texte sans rapport, sans le dire.

Vérifié en direct sur les trois langues :

| | 🇮🇹 | 🇬🇧 | 🇫🇷 |
| --- | --- | --- | --- |
| 2 · Symptôme | 3 fiches | 7 fiches | 2 fiches |
| 3 · Fusible F17 | ✅ | ✅ | ✅ |
| 4 · Schéma airbag | ✅ | ✅ | ❌ **pas de dessin** |
| 5 · Agrandir | ✅ | ✅ | ❌ rien à agrandir |
| 6 · Remise à zéro | ✅ | ✅ | ✅ |

Si le client est francophone, demande le schéma **en italien** au milieu du reste : c'est
une démonstration de plus, pas une faiblesse — il reconnaît la langue sans aucun bouton.
