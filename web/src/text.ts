export const list = (xs: string[]) =>
  xs.length < 2 ? xs.join('') : `${xs.slice(0, -1).join(', ')} and ${xs[xs.length - 1]}`

export const note = (n: string) => (/^[A-Z][a-z]+$/.test(n) ? n.toLowerCase() : n)

export const shares = (notes: string[]) => (notes.length ? `shares ${list(notes.map(note))}` : '')
