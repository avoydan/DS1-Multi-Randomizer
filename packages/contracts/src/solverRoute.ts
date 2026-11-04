/**
 * Solver route (docs/SOLVER_WITNESS.md): the human-followable proof of WHY the solver believes a generated Fog world is traversable.
 *
 * The data is recorded by FogHost inside the very search that accepted the world (`solver-witness.json`); this module only describes it. Nothing here
 * re-solves, interprets or corrects the graph: an edge is shown with the metadata it carries, so the first step that cannot be done in the game identifies
 * the graph/model defect. Pure and deterministic: the same document renders to the same text. Source identifiers are English by design (they are the
 * graph's own names); the panel chrome is localized by the application.
 */

export const SOLVER_WITNESS_FORMAT = 'ds1tr-solver-witness';
export const SOLVER_WITNESS_SCHEMA_VERSION = 1;
export const SOLVER_WITNESS_FILE_NAME = 'solver-witness.json';
export const SOLVER_ROUTE_FILE_NAME = 'solver-route.md';

export interface SolverGate {
  area: string;
  /** The physical object / warp name (for example `o4950_0000`); null for an in-map link. */
  object: string | null;
  /** The catalog's own description of the gate (project transition id), or "in map" / "hard skip" for an in-map link. */
  text: string | null;
  tags: string[];
  sideCondition: string | null;
  /** The Area on the opposite side of the same physical gate. */
  otherSideArea: string | null;
  areaIsBossArena: boolean;
  /** This side starts the boss encounter. */
  bossTriggerSide: boolean;
}

export interface SolverRequirement {
  name: string;
  kind: 'area' | 'key-item' | 'boss-completion';
  reachedInArea: string | null;
  satisfiedAtSequence: number | null;
  item: string | null;
}

export type SolverEdgeKind = 'randomized-fog' | 'fixed-fog' | 'warp' | 'door' | 'fixed-route' | 'glitched-route';

export interface SolverEdge {
  kind: SolverEdgeKind;
  label: string;
  randomized: boolean;
  /** `vanilla`: the object's exit side leads to its own other side. `shuffled`: it joins a different object. Null for an in-map link. */
  pairing: 'vanilla' | 'shuffled' | null;
  glitched: boolean;
  /** No edge from the arrival Area back to the exit Area exists in the graph. (An in-map link has no pair, but the way back is often a separate edge.) */
  oneWay: boolean;
  /** The way back in the graph: none, always available, or only under a condition. */
  reverse: 'none' | 'unconditional' | 'conditional';
  reverseCondition: string | null;
  /** The object (or "in map") of that edge back. */
  reverseVia: string | null;
  qualifiers: string[];
  exit: SolverGate;
  arrival: SolverGate;
  requires: string | null;
  requirements: SolverRequirement[];
  satisfiedBy: string[];
}

export interface SolverItem {
  name: string;
  gameName: string | null;
  gameItemId: string | null;
  defaultArea: string | null;
  solverArea: string | null;
  /** The catalog's default Area differs from the Area of the lot the solver found. */
  moved: boolean;
  lots: number[];
  tags: string[];
}

export interface SolverReach {
  sequence: number;
  pass: number;
  area: string;
  areaTags: string[];
  fromArea: string | null;
  edge: SolverEdge | null;
  keyItemsGained: string[];
}

export interface SolverCompletion {
  sequence: number;
  name: string;
  encounterArea: string;
  eventId: number;
  triggerEntrance: string;
  triggerEdgeFrom: string;
}

export interface SolverStep {
  number: number;
  type: 'start' | 'cross' | 'acquire' | 'boss-completion';
  area: string;
  sequence: number;
  returnTo: string | null;
  edge: SolverEdge | null;
  item: SolverItem | null;
  completion: SolverCompletion | null;
  notes: string[];
}

export interface SolverWitnessContext {
  seedCode: string;
  generationProfile: string;
  appVersion: string;
  masterSeed: string | null;
  fogSeed: number | null;
  combinedSeed: string | null;
  /** Zero-based attempts of the accepted generation (combined engine, Fog). */
  combinedAttempt: number | null;
  fogAttempt: number | null;
  /** All attempts recorded for the run, in order, with their result. */
  attempts: { combinedAttempt: number; fogAttempt: number; fogSeed: number | null; result: string }[];
}

/** The persisted document: FogHost's witness plus the run context the backend knows. Deterministic (no timestamps, no paths). */
export interface SolverWitnessDocument {
  format: typeof SOLVER_WITNESS_FORMAT;
  schemaVersion: number;
  hostVersion: string;
  annotationSource: string;
  claim: {
    statement: string;
    startArea: string;
    startName: string | null;
    areasTotal: number;
    areasReached: number;
    optionalAreasNotRequired: string[];
    unreached: string[];
    fogOptions: string[];
    glitchedLogicEnabled: boolean;
  };
  assumptions: string[];
  target: { area: string; reason: string };
  keyItems: SolverItem[];
  reachTree: SolverReach[];
  bossCompletions: SolverCompletion[];
  mainRoute: SolverStep[];
  context?: SolverWitnessContext;
}

export function isSolverWitnessDocument(value: unknown): value is SolverWitnessDocument {
  const doc = value as Partial<SolverWitnessDocument> | null;
  return !!doc && doc.format === SOLVER_WITNESS_FORMAT && doc.schemaVersion === SOLVER_WITNESS_SCHEMA_VERSION
    && Array.isArray(doc.mainRoute) && Array.isArray(doc.reachTree) && !!doc.claim && !!doc.target;
}

export interface SolverRouteRenderOptions {
  /** Append the full proof tree (the edge that first reached every Area). Default: false (the route only). */
  includeTree?: boolean;
}

/** "TWO-WAY (graph also has B -> A via ...)" or "ONE-WAY (no edge back in the graph)", with the kind of link and its pairing. */
export function directionText(edge: SolverEdge): string {
  const pairing = edge.pairing === null ? '' : `, ${edge.pairing} pairing`;
  const kind = `${edge.randomized ? 'randomized' : 'fixed'}${pairing}`;
  if (edge.reverse === 'none') return `ONE-WAY (no edge back in the graph; ${kind})`;
  const via = edge.reverseVia && edge.reverseVia !== 'in map' ? ` via ${edge.reverseVia}` : ' by an in-map link';
  const back = `${edge.arrival.area} -> ${edge.exit.area}${via}`;
  return edge.reverse === 'conditional'
    ? `TWO-WAY (the graph also has ${back}, only when ${edge.reverseCondition ?? 'a condition holds'}; ${kind})`
    : `TWO-WAY (the graph also has ${back}, always; ${kind})`;
}

const objectText = (gate: SolverGate): string => {
  if (gate.object === null) return `${gate.area} (in-map link)`;
  const text = gate.text && gate.text !== gate.object ? ` "${gate.text}"` : '';
  return `${gate.area} — object ${gate.object}${text}`;
};

const gateNotes = (gate: SolverGate): string => {
  const notes: string[] = [];
  if (gate.bossTriggerSide) notes.push('boss-encounter side');
  else if (gate.areaIsBossArena) notes.push('boss arena');
  if (gate.otherSideArea) notes.push(`other side of this gate: ${gate.otherSideArea}`);
  return notes.length ? `  [${notes.join('; ')}]` : '';
};

/** "step N" for the step that proved the requirement, or an explicit note when it is not a step of this route. */
function requirementLine(requirement: SolverRequirement, stepOfSequence: Map<number, number>, acquireStep: Map<string, number>, completionStep: Map<string, number>): string {
  const step = requirement.kind === 'key-item'
    ? acquireStep.get(requirement.name)
    : requirement.kind === 'boss-completion'
      ? completionStep.get(requirement.name)
      : requirement.satisfiedAtSequence === null ? undefined : stepOfSequence.get(requirement.satisfiedAtSequence);
  const where = step === undefined ? '' : ` — step ${step}`;
  switch (requirement.kind) {
    case 'key-item': return `key item ${requirement.name} (held by Area ${requirement.reachedInArea ?? '?'})${where}`;
    case 'boss-completion': return `boss completion ${requirement.name} (encounter Area ${requirement.reachedInArea ?? '?'})${where}`;
    default: return `Area ${requirement.name} reached${where}`;
  }
}

function describeItem(item: SolverItem): string {
  const name = item.gameName ? `${item.gameName} (${item.name})` : item.name;
  return name;
}

/** The route steps as plain text lines, one block per step. */
export function renderSolverSteps(document: SolverWitnessDocument): string[] {
  const stepOfSequence = new Map<number, number>();
  const acquireStep = new Map<string, number>();
  const completionStep = new Map<string, number>();
  for (const step of document.mainRoute) {
    if (step.type === 'boss-completion' && step.completion) completionStep.set(step.completion.name, step.number);
    if (step.type === 'start' || step.type === 'cross') stepOfSequence.set(step.sequence, step.number);
    if (step.type === 'acquire' && step.item) acquireStep.set(step.item.name, step.number);
  }
  const blocks: string[] = [];
  for (const step of document.mainRoute) {
    const lines: string[] = [];
    if (step.type === 'start') {
      lines.push(`${step.number}. START — ${step.area}`);
      lines.push('   The player starts here.');
    } else if (step.type === 'cross' && step.edge) {
      const edge = step.edge;
      lines.push(`${step.number}. ${edge.label.toUpperCase()}`);
      if (edge.glitched) lines.push('   ⚠ GLITCHED LOGIC REQUIRED');
      if (step.returnTo) lines.push(`   First be in: ${step.returnTo} (the solver assumes any Area it already reached can be entered again)`);
      lines.push(`   FROM SIDE: ${objectText(edge.exit)}${gateNotes(edge.exit)}`);
      lines.push(`   TO SIDE:   ${objectText(edge.arrival)}${gateNotes(edge.arrival)}`);
      lines.push(`   DIRECTION: ${directionText(edge)}`);
      if (edge.requirements.length === 0) lines.push('   SOLVER REQUIREMENTS: none');
      else {
        lines.push(`   SOLVER REQUIREMENTS: ${edge.requires}`);
        for (const requirement of edge.requirements) lines.push(`     - ${requirementLine(requirement, stepOfSequence, acquireStep, completionStep)}`);
      }
    } else if (step.type === 'acquire' && step.item) {
      lines.push(`${step.number}. ACQUIRE — ${describeItem(step.item)}`);
      if (step.item.lots.length) lines.push(`   SOURCE: item lot${step.item.lots.length === 1 ? '' : 's'} ${step.item.lots.join(', ')}, in Area ${step.area}`);
      else lines.push(`   SOURCE: no item lot was found for it; the solver uses its default Area ${step.area}`);
      if (step.item.moved) lines.push(`   NOTE: the catalog's default Area for this item is ${step.item.defaultArea ?? '?'}; the solver uses the Area of the lot it found`);
      lines.push('   The solver considers it obtained once this Area has been reached.');
    } else if (step.type === 'boss-completion' && step.completion) {
      lines.push(`${step.number}. BOSS COMPLETION ASSUMED — ${step.completion.name}`);
      lines.push(`   SOURCE: encounter in Area ${step.completion.encounterArea} entered through ${step.completion.triggerEntrance} (event ${step.completion.eventId})`);
      lines.push('   The solver assumes the boss is defeated; the game sets the real flag only when it dies.');
    }
    blocks.push(lines.join('\n'));
  }
  return blocks;
}

function treeLine(reach: SolverReach): string {
  if (!reach.edge) return `${String(reach.sequence).padStart(3)}  ${reach.area}  START`;
  const edge = reach.edge;
  const from = edge.exit.object ?? 'in-map';
  const to = edge.arrival.object ?? 'in-map';
  const requires = edge.requires ? `  requires ${edge.requires}` : '';
  const flags = `${edge.oneWay ? 'one-way' : edge.reverse === 'conditional' ? 'two-way (conditional)' : 'two-way'}${edge.glitched ? ', GLITCHED' : ''}`;
  return `${String(reach.sequence).padStart(3)}  ${reach.area}  <- ${reach.fromArea} via ${edge.label} (${from} -> ${to}; ${flags})${requires}`;
}

/** The full proof tree as plain text lines: the edge that first reached every Area, in the solver's own order. */
export function renderSolverTree(document: SolverWitnessDocument): string[] {
  return document.reachTree.map(treeLine);
}

/** The full Markdown document (`solver-route.md`). */
export function renderSolverRoute(document: SolverWitnessDocument, options: SolverRouteRenderOptions = {}): string {
  const context = document.context;
  const out: string[] = [];
  out.push('# Solver route — why this seed is solvable', '');
  if (context) {
    out.push(`- Seed Code: ${context.seedCode}`);
    out.push(`- Generation profile: ${context.generationProfile}`);
    out.push(`- Fog seed: ${context.fogSeed ?? 'n/a'} (Fog attempt ${context.fogAttempt === null ? 'n/a' : context.fogAttempt + 1}, combined attempt ${context.combinedAttempt === null ? 'n/a' : context.combinedAttempt + 1}; ${context.attempts.length} attempt(s) recorded)`);
    out.push(`- App version: ${context.appVersion}`);
  }
  out.push(`- Annotation source: ${document.annotationSource}, FogHost ${document.hostVersion}`);
  out.push(`- Fog options: ${document.claim.fogOptions.join(' ') || 'none'}${document.claim.glitchedLogicEnabled ? ' (GLITCHED LOGIC IS ON)' : ' (glitched logic is off)'}`);
  out.push(`- Start Area: ${document.claim.startArea}${document.claim.startName ? ` ("${document.claim.startName}")` : ''}`);
  out.push(`- Solver claim: ${document.claim.statement} ${document.claim.areasReached} of ${document.claim.areasTotal} Areas reached` +
    `${document.claim.unreached.length ? `, UNREACHED: ${document.claim.unreached.join(', ')}` : ''}.`);
  out.push(`- Target of this route: ${document.target.area} — ${document.target.reason}`, '');
  out.push('Follow the steps in order. Each step names the gate you leave (FROM SIDE) and the gate you arrive at (TO SIDE). The first step that cannot be done in the game identifies the graph/model defect.', '');
  out.push('## Route', '');
  out.push(renderSolverSteps(document).join('\n\n'), '');
  out.push('## What the solver assumes', '');
  for (const assumption of document.assumptions) out.push(`- ${assumption}`);
  out.push('');
  if (options.includeTree) {
    out.push('## Full proof tree (the edge that first reached every Area, in the solver\'s order)', '', '```');
    out.push(...renderSolverTree(document));
    out.push('```', '');
  }
  return out.join('\n');
}
