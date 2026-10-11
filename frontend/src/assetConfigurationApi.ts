import {useEffect,useState} from 'react';
import {inventoryRequest} from './inventoryApi';
export type AssetType={id:string;code:string;name:string;icon:string;active:boolean;sortOrder:number;revision:number;categories?:string[]};
export type AssetField={id:string;assetTypeId:string|null;name:string;kind:string;helpText:string|null;required:boolean;active:boolean;sortOrder:number;choices:string[];revision:number};
export type FieldValues=Record<string,string|number|boolean|null>;
export type AssetConfiguration={types:AssetType[];fields:AssetField[];icons:string[]};
export const readConfiguration=(signal?:AbortSignal)=>inventoryRequest<AssetConfiguration>('configuration','GET',undefined,signal);
export function useAssetConfiguration(){const [configuration,setConfiguration]=useState<AssetConfiguration>({types:[],fields:[],icons:[]}),[error,setError]=useState(''),[loading,setLoading]=useState(true);useEffect(()=>{const c=new AbortController();readConfiguration(c.signal).then(v=>{if(!c.signal.aborted)setConfiguration({...v,types:[...v.types].sort((a,b)=>a.sortOrder-b.sortOrder||a.name.localeCompare(b.name)),fields:[...v.fields].sort((a,b)=>a.sortOrder-b.sortOrder)});}).catch(e=>{if(e.name!=='AbortError')setError(e.message);}).finally(()=>{if(!c.signal.aborted)setLoading(false);});return()=>c.abort();},[]);return {configuration,error,loading};}
export const supportedDocumentation=(code:string)=>!!code;
export function typeName(types:AssetType[],code:string,id?:string|null){return types.find(t=>id?t.id===id:t.code===code)?.name??code;}

