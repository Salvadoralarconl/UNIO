export type CompanyRole = 'Administrator'|'Editor'|'Viewer';
export type Capabilities={read:boolean;ownInventory:boolean;write:boolean;delete:boolean;approve:boolean;index:boolean;repair:boolean;settings:boolean;accountAdmin:boolean};
export type LocalProfile={username:string|null;firstName:string|null;lastName:string|null;passwordSet:boolean};
export type Session = {authenticationMode?:'Local'|'Oidc';localProfile?:LocalProfile|null;user:{id:string;displayName:string;email:string|null};activeCompany:{id:string;name:string;revision:number}|null;memberships:{companyId:string;name:string;role:CompanyRole}[];role:CompanyRole|null;isOperator:boolean;capabilities:Capabilities};
let generation = 0;
export function companyScopeGeneration(){return generation;}
export function companyScopeSignal(){return scope.signal;}
let scope = new AbortController();
let csrf:Promise<string>|null = null;
export function clearCompanyScope(clearStorage=true){generation++;scope.abort();scope=new AbortController();csrf=null;if(clearStorage)for(const storage of [localStorage,sessionStorage])try{for(const key of Object.keys(storage))if(key.startsWith('clary-'))storage.removeItem(key);}catch{/* Storage may be disabled by browser policy. */}}
function expired(){window.dispatchEvent(new Event('clary-session-expired'));}
function check(epoch:number){if(epoch!==generation)throw new DOMException('Company changed','AbortError');}
export async function authenticatedFetch(input:RequestInfo|URL,init:RequestInit={}):Promise<Response>{
 const epoch=generation;const signal=init.signal?AbortSignal.any([scope.signal,init.signal]):scope.signal;
 const headers=new Headers(input instanceof Request?input.headers:undefined);new Headers(init.headers).forEach((value,key)=>headers.set(key,value));
 const method=(init.method??(input instanceof Request?input.method:'GET')).toUpperCase();
 if(!['GET','HEAD','OPTIONS'].includes(method)){
  csrf??=authenticatedFetch('/api/session/antiforgery').then(async r=>{if(!r.ok)throw new Error('Session verification failed. Please retry.');return (await r.json()).token as string;}).catch(e=>{csrf=null;throw e;});
  headers.set('X-CSRF-TOKEN',await csrf);check(epoch);
 }
 const response=await fetch(input,{...init,method,headers,credentials:'include',signal});check(epoch);
 if(response.status===401){expired();throw new Error('Your session has ended. Please sign in again.');}
 if(response.status===403){window.dispatchEvent(new Event('clary-access-denied'));throw new Error('Company access changed. Reload your session.');}
 // Body consumption may complete after fetch resolves; guard that boundary too.
 for(const key of ['json','text','blob','arrayBuffer','formData'] as const){const read=response[key].bind(response);Object.defineProperty(response,key,{value:async()=>{const value=await read();check(epoch);return value;}});}
 return response;
}
export async function getSession(signal?:AbortSignal):Promise<Session>{const response=await authenticatedFetch('/api/session',{signal});if(!response.ok)throw new Error('Your session could not be loaded. Please retry.');return response.json();}
export async function selectCompany(id:string){const response=await authenticatedFetch('/api/session/company',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({companyId:id})});if(!response.ok)throw new Error('Company access could not be changed. Please retry.');csrf=null;return response.json();}
export async function logout(){const response=await authenticatedFetch('/api/session/logout',{method:'POST'});if(!response.ok)throw new Error('Sign out failed. Please retry.');clearCompanyScope();}

export async function acceptInvitation(token:string,signal?:AbortSignal){const response=await authenticatedFetch('/api/session/invitations/accept',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token}),signal});if(!response.ok){const body=await response.json().catch(()=>({}));throw new Error(body.error??'Invitation could not be accepted. Ask your administrator to check the invitation and account.');}const result=await response.json();if(result.freshLoginRequired!==true)throw new Error('Sign in again before opening your company.');return result;}
