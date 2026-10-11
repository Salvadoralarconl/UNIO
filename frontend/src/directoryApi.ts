import {authenticatedFetch} from './companyApi';
export type DepartmentDto={id:string;name:string;color:string;active:boolean;revision:number};
export type EmployeeDto={id:string;displayName:string;email:string|null;departmentId:string|null;active:boolean;revision:number};
export type LocationDto={id:string;name:string;parentId:string|null;active:boolean;revision:number};
export type DirectoryKinds={departments:DepartmentDto;employees:EmployeeDto;locations:LocationDto};
export type DirectoryPage<T>={items:T[];nextCursor:string|null};
export async function directoryRequest<T>(path:string,method='GET',body?:unknown,signal?:AbortSignal):Promise<T>{const r=await authenticatedFetch('/api/'+path,{method,signal,headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});if(!r.ok){const e=await r.json().catch(()=>({}));throw new Error(e.error??'Settings could not be loaded. Please retry.');}return r.json();}
export function listDirectory<K extends keyof DirectoryKinds>(kind:K,query='',cursor?:string|null,signal?:AbortSignal){const params=new URLSearchParams({query,limit:'100'});if(cursor)params.set('cursor',cursor);return directoryRequest<DirectoryPage<DirectoryKinds[K]>>('directory/'+kind+'?'+params,'GET',undefined,signal);}
export function saveDirectory<K extends keyof DirectoryKinds>(kind:K,input:Partial<DirectoryKinds[K]>,id?:string){return directoryRequest<DirectoryKinds[K]>('directory/'+kind+(id?'/'+id:''),id?'PUT':'POST',input);}
export async function allDirectory<K extends keyof DirectoryKinds>(kind:K,signal?:AbortSignal,query=''){const rows:DirectoryKinds[K][]=[];let cursor:string|null=null;do{const page:DirectoryPage<DirectoryKinds[K]>=await listDirectory(kind,query,cursor,signal);rows.push(...page.items);cursor=page.nextCursor;}while(cursor);return rows;}
