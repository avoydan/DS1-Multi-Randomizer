import type { TFunction } from 'i18next';
import type { PlanBehavior } from '@ds1tr/contracts';

/**
 * The plan preview in the current language. The backend sends stable keys next to its English sentence; a line without keys (an older
 * backend) is shown as it came.
 */
export function behaviorLabel(t: TFunction, line: PlanBehavior): string {
  return line.labelKey ? t(`plan.labels.${line.labelKey}` as never, { defaultValue: line.label }) as string : line.label;
}

export function behaviorValue(t: TFunction, line: PlanBehavior): string {
  if (!line.valueKey) return line.value;
  const pools = (line.params?.pools ?? '').split(',').filter(Boolean).map((id) => t(`plan.pools.${id}` as never) as string).join(', ');
  return t(`plan.values.${line.valueKey}` as never, { pools, defaultValue: line.value }) as string;
}
