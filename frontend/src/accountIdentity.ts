// Preserve Unicode code points and provide an empty-name fallback.
export function accountInitials(displayName:string|null|undefined){const parts=(displayName??'').trim().split(/\s+/u).filter(Boolean);return parts.slice(0,2).map(part=>Array.from(part)[0]).join('').toLocaleUpperCase()||'U';}
