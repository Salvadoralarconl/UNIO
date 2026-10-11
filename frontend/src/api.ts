import {authenticatedFetch} from './companyApi';
export type Health = { status: string; origin: string; liveReady: boolean; configurationError: string | null };
export type Citation = { passageId: string; quote: string };
export type BrieTicketTask = {taskId:string;status:string;message:string;choices?:{id:string;label:string}[]|null;ticketId?:string|null;confirmationToken?:string|null;recordKind?:string|null;recordId?:string|null};
export type Answer = { ticketTask?:BrieTicketTask|null; status: string; questions: string[]; steps: { text: string; citations: Citation[] }[]; citations: Citation[]; origin: string; error: string | null; conversationId?: string | null; evidenceNote?: string | null };
export type Passage = { id: string; revisionId: string; title: string; version: string; location: string; text: string };
export type DocumentInfo = { id: string; title: string; version: string; approval: string; indexing: string; error: string | null; isWiki?: boolean; isUploaded?: boolean };
export async function api<T>(path: string, body?: unknown): Promise<T> {
  const multipart = body instanceof FormData;
  const response = await authenticatedFetch(path, { method: body === undefined ? 'GET' : 'POST', headers: body === undefined || multipart ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : multipart ? body : JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail ?? problem.error ?? 'The request failed. Check the local backend.');
  }
  return response.json();
}
export async function getHealth(signal: AbortSignal): Promise<Health> {
  const response = await authenticatedFetch('/api/health', { signal });
  if (!response.ok) throw new Error('The backend is unavailable. Start scripts/Start-Clary.ps1.');
  return response.json();
}

