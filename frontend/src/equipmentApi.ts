import {authenticatedFetch} from './companyApi';
export const legacyEquipmentTypes=['Computer','Printer','Monitor','Network','Accessory'] as const;
export type EquipmentLine={assetTypeId?:string|null;category:string;customCategory:string|null;quantity:number};
export type Kit={id:string;name:string;departmentId:string|null;active:boolean;revision:number;lines:EquipmentLine[];availability:(EquipmentLine&{available:number})[];canFulfill:boolean;fulfillableCount:number};
export type KitInput={name:string;departmentId:string|null;active:boolean;lines:EquipmentLine[];revision?:number};
export type EquipmentRequest={id:string;title:string;employeeId:string;departmentId:string|null;kitId:string;kitName:string;neededDate:string|null;notes:string|null;state:'Pending'|'Approved'|'Ready'|'Completed'|'Cancelled';ticketId:string;revision:number;createdAt:string;lines:EquipmentLine[];assetIds:string[];purpose?:'Equipment'|'Onboarding';locationId?:string|null};
export type RequestInput={title:string;employeeId:string;departmentId:string|null;kitId:string;neededDate:string|null;notes:string|null;immediateAssignment:boolean;purpose?:'Equipment'|'Onboarding';locationId?:string|null};
export type Ticket={id:string;requestId:string;title:string;state:string;revision:number;createdAt:string;updatedAt:string};
export class EquipmentError extends Error{constructor(message:string,public status:number){super(message);}}
export async function equipmentRequest<T>(path:string,method='GET',body?:unknown,signal?:AbortSignal):Promise<T>{const response=await authenticatedFetch('/api/equipment/'+path,{method,signal,headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});if(!response.ok){const error=await response.json().catch(()=>({}));throw new EquipmentError(error.error??'Equipment could not be loaded. Please retry.',response.status);}return response.json();}
export const listKits=(signal?:AbortSignal)=>equipmentRequest<Kit[]>('kits','GET',undefined,signal);
export const saveKit=(input:KitInput,id?:string,signal?:AbortSignal)=>equipmentRequest<Kit>('kits'+(id?'/'+id:''),id?'PUT':'POST',input,signal);
export const deleteKit=(id:string,revision:number,signal?:AbortSignal)=>equipmentRequest<{id:string;deleted:boolean}>('kits/'+id,'DELETE',{revision},signal);

