import type {FieldValues} from './assetConfigurationApi';
import {authenticatedFetch} from './companyApi';
export type Asset={assetTypeId?:string|null;assetTypeName?:string;customFields?:FieldValues;employeeId?:string|null;departmentId?:string|null;locationId?:string|null;id:string;assetTag:string;computer:string;serialNumber:string|null;ipAddress:string|null;assignee:string|null;department:string|null;location:string|null;status:string;revision:number;updatedAt:string;softwareStatus:string;lastSeen:string|null;category:string;customCategory?:string|null};
export type AssetInput={assetTypeId?:string|null;customFields?:FieldValues;employeeId?:string|null;departmentId?:string|null;locationId?:string|null;newAssigneeName?:string|null;clearEmployee?:boolean;clearDepartment?:boolean;clearLocation?:boolean;assetTag:string;computer:string;serialNumber:string;ipAddress:string;assignee:string;department:string;location:string;status:string;revision?:number;category:string;customCategory?:string|null};
export type Activity={id:string;assetId:string;computer:string;kind:string;createdAt:string};
export class InventoryError extends Error{constructor(message:string,public status:number){super(message);}}
export async function inventoryRequest<T>(path:string,method='GET',body?:unknown,signal?:AbortSignal):Promise<T>{const r=await authenticatedFetch('/api/inventory/'+path,{method,signal,headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});if(!r.ok){const e=await r.json().catch(()=>({}));throw new InventoryError(e.error??'Inventory could not be loaded. Please retry.',r.status);}return r.json();}
export const listAssets=(signal?:AbortSignal)=>inventoryRequest<Asset[]>('assets','GET',undefined,signal);
export const saveAsset=(input:AssetInput,id?:string,signal?:AbortSignal)=>inventoryRequest<Asset>('assets'+(id?'/'+id:''),id?'PUT':'POST',input,signal);
export const display=(v:string|null|undefined)=>v||'—';
export const date=(v:string|null)=>v?new Date(v).toLocaleString():'—';

export type Department={name:string;color:string};

