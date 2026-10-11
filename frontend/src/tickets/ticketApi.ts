import {authenticatedFetch} from '../companyApi';
import type {EquipmentLine} from '../equipmentApi';
export const ticketStatuses=['Pending','Approved','Assigned','In Progress','Complete','Canceled'] as const;
export const ticketTypes=['Support','Equipment','Onboarding','Repair','Retirement'] as const;
export type TicketStatus=typeof ticketStatuses[number];
export type TicketUser={id:string;displayName:string};
export type TicketAsset={id:string;name:string;assetTag:string;assetType:string;status:string;outcome:string;brand?:string|null;model?:string|null;available:boolean};
export type TicketDocument={id:string|null;title:string;available:boolean};
export type Ticket={priority?:string;id:string;requestId:string|null;title:string;description:string|null;type:string;status:TicketStatus;revision:number;dueDate:string|null;createdAt:string;creatorUserId:string|null;creatorName:string|null;approverName:string|null;approvedAt:string|null;recipientEmployeeId:string|null;recipientName:string|null;departmentId:string|null;departmentName:string|null;locationId:string|null;locationName:string|null;assignmentMode:'Users'|'All';assignees:TicketUser[];assets:TicketAsset[];documents:TicketDocument[];lines:EquipmentLine[];allowedActions:string[];checklist?:ChecklistItem[]};
export type TicketInput={priority?:string;title:string;description:string;type:string;dueDate:string|null;assignmentMode:'Users'|'All';assigneeIds:string[];recipientEmployeeId:string|null;departmentId:string|null;locationId:string|null;assetLinks:{assetId:string;outcome:string}[];documentIds:string[];kitId:string|null;checklist?:string[];revision?:number};
export type Page<T>={items:T[];nextCursor:string|null};
export type Comment={id:string;authorUserId:string;authorName:string;body:string;createdAt:string};
export type Activity={id:string;actorName:string;action:string;detail:string|null;createdAt:string};
export type TicketPrefill={assetId?:string;documentId?:string};
export async function ticketRequest<T>(path='',method='GET',body?:unknown,signal?:AbortSignal):Promise<T>{const response=await authenticatedFetch('/api/tickets'+path,{method,signal,headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined});if(!response.ok){const value=await response.json().catch(()=>({}));throw new Error(value.error??'Tickets could not be loaded. Please retry.');}return response.json();}
export const localDate=(value:string|null|undefined)=>value?new Date(value.length===10?value+'T12:00:00':value).toLocaleDateString(): '—';
export const localTime=(value:string)=>new Date(value).toLocaleString();
export function today(){const now=new Date();return `${now.getFullYear()}-${String(now.getMonth()+1).padStart(2,'0')}-${String(now.getDate()).padStart(2,'0')}`;}

export type ChecklistItem={id:string;text:string;completed:boolean;completedBy:string|null;completedAt:string|null};
export type TicketTemplate={id:string;name:string;type:string;instructions:string;assignmentMode:'Users'|'All';assigneeIds:string[];documentIds:string[];kitId:string|null;checklist:string[];revision:number};
