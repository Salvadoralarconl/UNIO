import InventoryToolbarActions from './InventoryToolbarActions';
import InventoryToolbarSearch from './InventoryToolbarSearch';
import {ticketRequest,ticketStatuses,ticketTypes,today,type TicketUser} from './tickets/ticketApi';
import {equipmentRequest,type EquipmentRequest} from './equipmentApi';

import {useEffect,useState} from 'react';

import {Download,RefreshCw} from 'lucide-react';

import AssetCategoryInput from './AssetCategoryInput';

import ComputerAssetDetails from './ComputerAssetDetails';

import {listAssets,type Asset} from './inventoryApi';

import {allDirectory,type EmployeeDto,type DepartmentDto,type LocationDto} from './directoryApi';

import {useAssetConfiguration,typeName} from './assetConfigurationApi';

import './inventory-reports.css';

type Subject='Assets'|'People'|'Locations'|'Departments'|'Onboarding'|'Tickets'|'User workload'|'Asset repairs';

type Row={id:string;values:Record<string,string>;asset?:Asset;requestId?:string;ticketId?:string};

const columns:Record<Subject,string[]>={Tickets:[],'User workload':[],'Asset repairs':[],Assets:['Asset tag','Equipment name','Asset type','Status','Person','Department','Location'],People:['Person','Email','Department','Asset count','Equipment'],Locations:['Location','Asset count','Equipment'],Departments:['Department','Asset count','Equipment'],Onboarding:['Ticket','Person','Department','Location','Start date','Status','Required assets','Assigned assets','Missing assets','Kit']};

columns.Tickets=['Ticket','Type','Status','Due date','Assigned to','Person','Location','Created by','Approved by','Approved date'];
columns['User workload']=['User','Total tickets','Open tickets','In progress','Overdue'];
columns['Asset repairs']=['Asset tag','Equipment name','Repair tickets','Open repairs','Last completed','Tickets'];
const ticketSubjects=['Tickets','User workload','Asset repairs'];
type ReportPage={items:{id:string;values:Record<string,string>;ticketId?:string}[];nextCursor:string|null;total:number};
const assigned=(a:Asset)=>!!(a.employeeId||a.assignee?.trim());

export default function InventoryReports({openOnboarding}:{openOnboarding?:(id:string)=>void}){

 const [search,setSearch]=useState('');
 const {configuration}=useAssetConfiguration();

 const [requests,setRequests]=useState<EquipmentRequest[]>([]),[from,setFrom]=useState(''),[to,setTo]=useState(''),[assets,setAssets]=useState<Asset[]>([]),[people,setPeople]=useState<EmployeeDto[]>([]),[departments,setDepartments]=useState<DepartmentDto[]>([]),[locations,setLocations]=useState<LocationDto[]>([]);

 const [subject,setSubject]=useState<Subject>('Assets'),[scope,setScope]=useState('All'),[type,setType]=useState(''),[department,setDepartment]=useState(''),[location,setLocation]=useState(''),[person,setPerson]=useState(''),[status,setStatus]=useState('');

 const [selected,setSelected]=useState<string[]>(columns.Assets),[busy,setBusy]=useState(true),[error,setError]=useState(''),[retry,setRetry]=useState(0),[details,setDetails]=useState<Asset|null>(null);

 useEffect(()=>{const c=new AbortController();setBusy(true);setError('');Promise.all([listAssets(c.signal),allDirectory('employees',c.signal),allDirectory('departments',c.signal),allDirectory('locations',c.signal),equipmentRequest<EquipmentRequest[]>('requests','GET',undefined,c.signal)]).then(([a,p,d,l,r])=>{if(!c.signal.aborted){setRequests(r);setAssets(a);setPeople(p);setDepartments(d);setLocations(l);}}).catch(e=>{if(!c.signal.aborted)setError(e.message);}).finally(()=>{if(!c.signal.aborted)setBusy(false);});return()=>c.abort();},[retry]);

 const [ticketRows,setTicketRows]=useState<Row[]>([]),[reportTotal,setReportTotal]=useState(0),[reportCursor,setReportCursor]=useState<string|null>(null),[reportBusy,setReportBusy]=useState(false),[reportUsers,setReportUsers]=useState<TicketUser[]>([]),[assignee,setAssignee]=useState('');
 const ticketReport=ticketSubjects.includes(subject);
 const reportQuery='?search='+encodeURIComponent(search)+'&subject='+encodeURIComponent(subject)+'&limit=50&status='+encodeURIComponent(status)+'&type='+encodeURIComponent(type)+(person?'&recipientId='+encodeURIComponent(person):'')+(location?'&locationId='+encodeURIComponent(location):'')+(assignee?'&assigneeId='+encodeURIComponent(assignee):'')+'&overdue='+(scope==='Overdue')+'&today='+today();
 useEffect(()=>{if(!ticketReport)return;const controller=new AbortController();setReportBusy(true);setError('');setTicketRows([]);Promise.all([ticketRequest<ReportPage>('/reports'+reportQuery,'GET',undefined,controller.signal),ticketRequest<TicketUser[]>('/assignees','GET',undefined,controller.signal)]).then(([page,users])=>{setTicketRows(page.items);setReportTotal(page.total);setReportCursor(page.nextCursor);setReportUsers(users);}).catch(e=>{if(!controller.signal.aborted)setError(e.message);}).finally(()=>{if(!controller.signal.aborted)setReportBusy(false);});return()=>controller.abort();},[ticketReport,reportQuery,retry]);
 async function moreReport(){if(!reportCursor||reportBusy)return;setReportBusy(true);try{const page=await ticketRequest<ReportPage>('/reports'+reportQuery+'&cursor='+encodeURIComponent(reportCursor));setTicketRows(rows=>[...rows,...page.items]);setReportCursor(page.nextCursor);}catch(e){setError((e as Error).message);}finally{setReportBusy(false);}}

 const match=(id:string|null|undefined,name:string|null,rows:{id:string;name:string}[],chosen:string)=>!chosen||(id?id===chosen:rows.filter(r=>r.name===name).length===1&&rows.find(r=>r.id===chosen)?.name===name);

 const personRows=people.map(p=>({id:p.id,name:p.displayName}));

 const filtered=assets.filter(a=>(!type||a.assetTypeId===type)&&(!status||a.status===status)&&match(a.departmentId,a.department,departments,department)&&match(a.locationId,a.location,locations,location)&&match(a.employeeId,a.assignee,personRows,person));

 const scopeMatch=(count:number)=>scope==='All'||(scope==='With assets'?count>0:count===0);

 let rows:Row[]=[];

 if(ticketReport)rows=ticketRows.map(r=>({...r,asset:subject==='Asset repairs'?assets.find(a=>a.id===r.id):undefined}));
 else if(subject==='Onboarding')rows=requests.filter(r=>r.purpose==='Onboarding'&&(!department||r.departmentId===department)&&(!location||r.locationId===location)&&(!person||r.employeeId===person)&&(!status||r.state===status)&&(!from||!!r.neededDate&&r.neededDate>=from)&&(!to||!!r.neededDate&&r.neededDate<=to)&&(!type||r.lines.some(l=>l.assetTypeId===type))).flatMap(r=>{const required=r.lines.reduce((n,l)=>n+l.quantity,0),assignedCount=assets.filter(a=>r.assetIds.includes(a.id)&&a.employeeId===r.employeeId).length,missing=Math.max(0,required-assignedCount);if(scope==='Missing equipment'&&(missing===0||r.state==='Cancelled')||scope==='Ready'&&r.state!=='Ready'||scope==='Completed'&&r.state!=='Completed')return [];return [{id:r.id,requestId:r.id,values:{Ticket:r.title,Person:people.find(p=>p.id===r.employeeId)?.displayName??'Archived person',Department:departments.find(d=>d.id===r.departmentId)?.name??'—',Location:locations.find(l=>l.id===r.locationId)?.name??'—','Start date':r.neededDate??'—',Status:r.state,'Required assets':String(required),'Assigned assets':String(assignedCount),'Missing assets':String(missing),Kit:r.kitName}}];});

 else if(subject==='Assets')rows=filtered.filter(a=>scope==='All'||(scope==='Assigned'?assigned(a):!assigned(a))).map(a=>({id:a.id,asset:a,values:{'Asset tag':a.assetTag,'Equipment name':a.computer,'Asset type':typeName(configuration.types,a.category,a.assetTypeId),'Status':a.status,'Person':a.assignee??'—','Department':a.department??'—','Location':a.location??'—'}}));

 else {

  const entities=subject==='People'?people.map(p=>({id:p.id,name:p.displayName,email:p.email,departmentId:p.departmentId})):subject==='Locations'?locations:departments;

  rows=entities.filter(e=>subject==='People'?(!person||e.id===person)&&(!department||('departmentId' in e&&e.departmentId===department)):subject==='Locations'?(!location||e.id===location):(!department||e.id===department)).flatMap(e=>{

   const linked=filtered.filter(a=>subject==='People'?match(a.employeeId,a.assignee,personRows,e.id):subject==='Locations'?match(a.locationId,a.location,locations,e.id):match(a.departmentId,a.department,departments,e.id));

   if(!scopeMatch(linked.length))return [];

   return [{id:e.id,values:{Person:e.name,Email:'email' in e?e.email??'—':'—',Department:subject==='Departments'?e.name:'departmentId' in e?departments.find(d=>d.id===e.departmentId)?.name??'—':'—',Location:e.name,'Asset count':String(linked.length),Equipment:linked.map(a=>a.computer+' ('+a.assetTag+')').join('; ')||'—'}}];

  });

 }

 rows=rows.filter(row=>Object.values(row.values).some(value=>String(value).toLowerCase().includes(search.trim().toLowerCase())));
 rows.sort((a,b)=>(a.values[selected[0]]??'').localeCompare(b.values[selected[0]]??'',undefined,{numeric:true}));

 async function exportCsv(){let exported=rows;if(ticketReport){setReportBusy(true);try{let page=await ticketRequest<ReportPage>('/reports'+reportQuery);exported=page.items;while(page.nextCursor){page=await ticketRequest<ReportPage>('/reports'+reportQuery+'&cursor='+encodeURIComponent(page.nextCursor));exported.push(...page.items);}}catch(e){setError((e as Error).message);return;}finally{setReportBusy(false);}}const quote=(s:string)=>'"'+(/^[=+\-@\t\r]/.test(s)?"'":'')+s.replaceAll('"','""')+'"';const csv='\uFEFF'+[selected,...exported.map(r=>selected.map(c=>r.values[c]??''))].map(r=>r.map(quote).join(',')).join('\r\n');const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download='inventory-'+subject.toLowerCase()+'.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}

 function choice(label:string,value:string,options:{id:string;label:string}[],change:(id:string)=>void){return <AssetCategoryInput label={label} value={options.find(o=>o.id===value)?.label??''} options={options} allowCustom={false} disabled={busy} onChange={(_,id)=>change(id??'')}/>;}

 return <section className="equipment-page inventory-reports-page" aria-label="Reports workspace"><InventoryToolbarSearch value={search} onChange={setSearch} label="Search reports" placeholder="Search report results…"/><InventoryToolbarActions><button className="equipment-secondary" disabled={busy} onClick={()=>setRetry(n=>n+1)}><RefreshCw size={15}/>Refresh data</button></InventoryToolbarActions><section className="equipment-panel report-controls" aria-label="Report parameters"><div className="report-filter-grid">

 {choice('Report on',subject,['Assets','People','Locations','Departments','Onboarding','Tickets','User workload','Asset repairs'].map(s=>({id:s,label:s})),id=>{const next=id as Subject;if(!columns[next])return;setSubject(next);setScope('All');setStatus('');setType('');setAssignee('');setSelected(columns[next]);})}

 {choice('Include',scope,(ticketReport?['All','Overdue']:subject==='Onboarding'?['All','Missing equipment','Ready','Completed']:subject==='Assets'?['All','Assigned','Unassigned']:['All','With assets','Without assets']).map(s=>({id:s,label:s})),id=>setScope(id||'All'))}

 {ticketReport?choice('Ticket type',type,ticketTypes.map(t=>({id:t,label:t})),setType):choice('Asset type',type,configuration.types.map(t=>({id:t.id,label:t.name})),setType)}

 {choice('Status',status,(ticketReport?[...ticketStatuses]:subject==='Onboarding'?['Pending','Approved','Ready','Completed','Cancelled']:['Available','Assigned','In repair','Retired']).map(s=>({id:s,label:s})),setStatus)}

 {!ticketReport&&choice('Department',department,departments.map(d=>({id:d.id,label:d.name})),setDepartment)}{ticketReport&&choice('Task assignee',assignee,reportUsers.map(u=>({id:u.id,label:u.displayName})),setAssignee)}

 {choice('Location',location,locations.map(l=>({id:l.id,label:l.name})),setLocation)}

 {choice('Person',person,people.map(p=>({id:p.id,label:p.displayName})),setPerson)}{subject==='Onboarding'&&<><label className="report-date">Start date from<input type="date" value={from} onChange={e=>setFrom(e.target.value)}/></label><label className="report-date">Start date to<input type="date" value={to} min={from} onChange={e=>setTo(e.target.value)}/></label></>}

 </div><fieldset className="report-columns"><legend>Columns</legend>{columns[subject].map(c=><label key={c}><input type="checkbox" checked={selected.includes(c)} disabled={selected.length===1&&selected.includes(c)} onChange={()=>setSelected(old=>columns[subject].filter(col=>col===c?!old.includes(col):old.includes(col)))}/>{c}</label>)}</fieldset><footer><span>{ticketReport?'Shared tickets are counted separately in user workload.':'Asset filters apply to the assets counted for each person, location, or department.'}</span><button className="equipment-secondary" onClick={()=>{setAssignee('');setScope('All');setFrom('');setTo('');setType('');setStatus('');setDepartment('');setLocation('');setPerson('');setSelected(columns[subject]);}}>Reset parameters</button></footer></section>

 <section className="equipment-panel report-results" aria-label="Report results"><header><h2>{subject} report</h2><span>{busy||reportBusy?'Loading…':error?'Unavailable':(ticketReport?reportTotal:rows.length)+' results'}</span><button className="equipment-secondary" disabled={busy||reportBusy||!!error||!rows.length} onClick={()=>void exportCsv()}><Download size={15}/>Export CSV</button></header>{error?<div role="alert">{error}<button onClick={()=>setRetry(n=>n+1)}>Retry</button></div>:busy||reportBusy?<p role="status">Loading report data…</p>:<div className="report-table"><table><thead><tr style={{gridTemplateColumns:`repeat(${selected.length},minmax(140px,1fr))`}}>{selected.map(c=><th key={c}>{c}</th>)}</tr></thead><tbody>{rows.map(r=><tr key={r.id} style={{gridTemplateColumns:`repeat(${selected.length},minmax(140px,1fr))`}}>{selected.map(c=><td key={c}>{r.ticketId&&c==='Ticket'?<button className="inventory-asset-link" onClick={()=>window.dispatchEvent(new CustomEvent('open-ticket',{detail:r.ticketId}))}>{r.values[c]}</button>:r.requestId&&c==='Ticket'&&openOnboarding?<button className="inventory-asset-link" onClick={()=>openOnboarding(r.requestId!)}>{r.values[c]}</button>:r.asset&&c==='Equipment name'?<button className="inventory-asset-link" onClick={()=>setDetails(r.asset!)}>{r.values[c]}</button>:r.values[c]}</td>)}</tr>)}{!rows.length&&<tr><td>No matching results. Try changing your parameters.</td></tr>}</tbody></table>{ticketReport&&reportCursor&&<button className="equipment-secondary" disabled={reportBusy} onClick={()=>void moreReport()}>Load more results</button>}</div>}</section>{details&&<ComputerAssetDetails readOnly asset={details} close={()=>setDetails(null)} edit={()=>setDetails(null)}/>}</section>;

}



