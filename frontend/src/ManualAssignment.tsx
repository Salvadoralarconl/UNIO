import {useEffect,useState} from 'react';
import {listAssets,type Asset} from './inventoryApi';
import {type EquipmentRequest} from './equipmentApi';
import {type AssetType} from './assetConfigurationApi';
import EquipmentModal from './EquipmentModal';
import AssetCategoryInput from './AssetCategoryInput';
export default function ManualAssignment({request,types,busy,error,close,confirm}:{request:EquipmentRequest;types:AssetType[];busy:boolean;error:string;close:()=>void;confirm:(ids:string[])=>void}){
 const slots=request.lines.flatMap(l=>Array.from({length:l.quantity},()=>l));
 const [assets,setAssets]=useState<Asset[]>([]),[chosen,setChosen]=useState<string[]>(slots.map(()=>'')),[loading,setLoading]=useState(true),[loadError,setLoadError]=useState('');
 useEffect(()=>{const c=new AbortController();listAssets(c.signal).then(a=>{if(!c.signal.aborted)setAssets(a.filter(a=>a.status==='Available'&&!a.employeeId&&!a.assignee?.trim()));}).catch(e=>{if(!c.signal.aborted)setLoadError(e.message);}).finally(()=>{if(!c.signal.aborted)setLoading(false);});return()=>c.abort();},[]);
 return <EquipmentModal title="Choose available assets" close={close}><form onSubmit={e=>{e.preventDefault();if(chosen.every(Boolean)&&!busy)confirm(chosen);}}><p>Choose one device per required item. All assets are assigned together after availability is checked.</p>{slots.map((l,i)=><AssetCategoryInput key={i} label={(types.find(t=>t.id===l.assetTypeId||t.code===l.category)?.name??l.category)+' '+(i+1)} required value={assets.find(a=>a.id===chosen[i])?.computer??''} options={assets.filter(a=>(l.assetTypeId?a.assetTypeId===l.assetTypeId:a.category===l.category)&&(!l.customCategory||a.customCategory?.trim().toLowerCase()===l.customCategory.trim().toLowerCase())&&(!chosen.includes(a.id)||chosen[i]===a.id)).map(a=>({id:a.id,label:a.computer,description:a.assetTag}))} allowCustom={false} disabled={busy||loading} onChange={(_,id)=>setChosen(old=>old.map((v,n)=>n===i?id??'':v))}/>)}{loading&&<p role="status">Loading available inventory…</p>}{(error||loadError)&&<p role="alert">{error||loadError}</p>}<footer><button type="button" disabled={busy} onClick={close}>Cancel</button><button className="equipment-primary" disabled={busy||loading||!!loadError||!chosen.every(Boolean)}>{busy?'Assigning…':'Assign selected assets'}</button></footer></form></EquipmentModal>;
}
