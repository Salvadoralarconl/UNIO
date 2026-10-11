import {authenticatedFetch,type LocalProfile} from './companyApi';
export type PlanKind='Ten'|'Twenty'|'Fifty'|'Hundred'|'Custom'|'Unlimited';
export type Seats={enabled:number;pending:number;limit:number|null;remaining:number|null;overLimit:boolean};
export type CredentialLink={userId:string;expiresAt:string;link:string};
export type ProvisionedAccount={account:Account;setup:CredentialLink|null};
export type ProvisionedCompany={company:Company;administrator:ProvisionedAccount};
export type Account={localProfile?:LocalProfile|null;id:string;displayName:string;email:string|null;role:'Administrator'|'Editor'|'Viewer';enabled:boolean;revision:number;lastActivity:string|null;online:boolean};
export type AccountInput={issuer?:string;subject?:string;displayName?:string;firstName?:string;lastName?:string;email:string|null;role:Account['role'];enabled:boolean;revision?:number};
export type Company={id:string;name:string;active:boolean;revision:number;plan:PlanKind;limit:number|null;seats:Seats;onlineUsers:number;lastActivity:string|null;needsAdministrator:boolean};
export type Subscription={kind:PlanKind;limit:number|null;revision:number;seats:Seats};
export type Invitation={id:string;email:string;role:'Editor'|'Viewer';status:string;revision:number;createdAt:string;expiresAt:string;issuer:string|null;subject:string|null};
export type Page<T>={items:T[];nextCursor:string|null};
export class AccountError extends Error {constructor(message:string,public status:number,public usage?:Seats){super(message);}}
export async function accountRequest<T>(path:string,method='GET',body?:unknown,signal?:AbortSignal):Promise<T>{
 const response=await authenticatedFetch('/api/'+path,{method,signal,headers:body===undefined?{}:{'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});
 if(!response.ok){const p=await response.json().catch(()=>({}));throw new AccountError(p.code==='seat_limit_reached'?'Seat limit reached. Review current usage before adding or enabling an account.':p.error??'Account request failed. Please retry.',response.status,p.usage);}
 return response.json();
}
export function accountScope(company?:string){return company?'operator/companies/'+encodeURIComponent(company):'company';}
export function activity(value:string|null){return value?new Date(value).toLocaleString():'No activity';}
