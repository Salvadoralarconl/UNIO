import {authenticatedFetch,companyScopeSignal,companyScopeGeneration} from './companyApi';
export type SetupDetails={purpose:'Setup'|'Reset'|'Invitation';username:string|null;firstName:string|null;lastName:string|null;expiresAt:string};
export type SetupInput={token:string;username:string;firstName:string;lastName:string;password:string;confirmPassword:string};
// Anonymous endpoints have their own antiforgery exchange; they never bypass company authorization.
export async function anonymousRequest<T>(path:string,body:unknown,signal?:AbortSignal):Promise<T>{
 const epoch=companyScopeGeneration();const combined=signal?AbortSignal.any([signal,companyScopeSignal()]):companyScopeSignal();
 const check=()=>{if(combined.aborted||epoch!==companyScopeGeneration())throw new DOMException('Authentication changed','AbortError');};
 const verification=await fetch('/api/auth/antiforgery',{credentials:'include',signal:combined});check();if(!verification.ok)throw new Error('Session verification failed. Please retry.');const {token}=await verification.json();check();
 const response=await fetch('/api/auth/'+path,{method:'POST',credentials:'include',signal:combined,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token},body:JSON.stringify(body)});check();const result=await response.json().catch(()=>({}));check();if(!response.ok)throw new Error(result.error??'Unable to complete this request. Please retry.');return result;
}
export function login(username:string,password:string,signal?:AbortSignal){return anonymousRequest<{requiresSessionReload:boolean}>('login',{username:username.trim(),password},signal);}
export function setupDetails(token:string,signal?:AbortSignal){return anonymousRequest<SetupDetails>('setup/details',{token},signal);}
export function completeSetup(input:SetupInput,invitation:boolean,signal?:AbortSignal){return anonymousRequest<{freshLoginRequired:boolean}>(invitation?'invitations/complete':'setup',input,signal);}

export async function changeOwnPassword(input:{currentPassword:string;password:string;confirmPassword:string},signal:AbortSignal){
 const response=await authenticatedFetch('/api/auth/password',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(input),signal});
 const result=await response.json();if(!response.ok)throw new Error(result.error??'Unable to change password.');if(result.freshLoginRequired!==true)throw new Error('Unable to change password.');
}
