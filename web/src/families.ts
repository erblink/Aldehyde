import type { Family } from './api/types'

export type RGB = [number, number, number]

export const FAMILIES: { id: Family; name: string }[] = [
  { id: 'citrus', name: 'Citrus' },
  { id: 'floral', name: 'Floral' },
  { id: 'amber', name: 'Amber' },
  { id: 'green', name: 'Green' },
  { id: 'aquatic', name: 'Aquatic' },
  { id: 'woody', name: 'Woody' },
]

export function familyColours() {
  const css = getComputedStyle(document.documentElement)
  return Object.fromEntries(
    FAMILIES.map(({ id }) => {
      const hex = css.getPropertyValue(`--fam-${id}`).trim().slice(1)
      return [id, [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16))]
    }),
  ) as Record<Family, RGB>
}
