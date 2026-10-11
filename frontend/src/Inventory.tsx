import InventoryToolbarActions from './InventoryToolbarActions';
import InventoryToolbarSearch from './InventoryToolbarSearch';
import {useAssetConfiguration,typeName,supportedDocumentation} from './assetConfigurationApi';
import AssetTypeIcon from './AssetTypeIcon';
import './asset-configuration.css';
import {useCompanySession} from './CompanySession';
import {useEffect,useState} from 'react';
import {Search,ChevronRight,ScanLine,Box,X,Plus} from 'lucide-react';
import './inventory.css';
import TableColumnFilter from './TableColumnFilter';
import InventoryDocumentationLink from './InventoryDocumentationLink';
import {listAssets,type Asset,date,display} from './inventoryApi';
import ComputerAssetEditor from './ComputerAssetEditor';
import ComputerAssetDetails from './ComputerAssetDetails';
export default function Inventory({onOpenDocumentation,initialFilters={},title='Inventory'}:{onOpenDocumentation:(id:string)=>void;initialFilters?:Record<string,string>;title?:string}){
 const {permissions}=useCompanySession();const {configuration,error:configurationError}=useAssetConfiguration();const categories=configuration.types.filter(t=>t.active);
 const [assets,setAssets]=useState<Asset[]>([]),[loading,setLoading]=useState(true),[error,setError]=useState(''),[editing,setEditing]=useState<Asset|null|undefined>(undefined),[details,setDetails]=useState<Asset|null>(null);
 async function refresh(){setLoading(true);setError('');try{const rows=await listAssets();setAssets(rows);const target=new URLSearchParams(window.location.search).get("asset");if(target){const linked=rows.find(a=>a.id===target);if(linked)setDetails(linked);else setError("This asset is not accessible in the current company.");window.history.replaceState(null,"",window.location.pathname);}}catch(e){setError((e as Error).message);}finally{setLoading(false);}}
 useEffect(()=>{void refresh();},[]);
 const [search,setSearch]=useState(''),[category,setCategory]=useState<string|null>(null);const filtered=!!(search.trim()||category);
 const categoryType=Object.fromEntries(configuration.types.map(t=>[t.id,t.code]));
 const [columnFilters,setColumnFilters]=useState<Record<string,string>>(initialFilters);
 const columns=[['Asset tag','assetTag'],['Equipment','computer'],['Asset type','category'],['Category','customCategory'],['Status','status'],['Assignee','assignee'],['Department','department'],['Location','location'],['Updated','updatedAt']];
 const cell=(a:Asset,key:string)=>key==='category'?typeName(configuration.types,a.category,a.assetTypeId):key==='updatedAt'?(a.updatedAt?new Date(a.updatedAt).toLocaleDateString():'—'):display(a[key as keyof Asset] as string|null);
 const columnsActive=Object.values(columnFilters).some(Boolean);
 const rows=assets.filter(a=>columns.every(([,key])=>!columnFilters[key]||cell(a,key)===columnFilters[key])).filter(a=>(!category||categoryType[category]===a.category)&&[typeName(configuration.types,a.category,a.assetTypeId),a.computer,a.assetTag,a.customCategory,a.serialNumber,a.ipAddress,a.assignee,a.department,a.location].some(v=>v?.toLowerCase().includes(search.trim().toLowerCase())));
 return <section className="inventory-page" aria-label="Inventory workspace"><InventoryToolbarSearch value={search} onChange={setSearch} label="Search inventory" placeholder="Search assets, serial numbers, locations…"/><InventoryToolbarActions>{permissions.write&&<button className="equipment-primary" aria-label="Add asset" title="Add asset" onClick={()=>setEditing(null)}><Plus size={16}/>New asset</button>}</InventoryToolbarActions>
  
  {configurationError&&<p role="alert">{configurationError}</p>}{category&&!supportedDocumentation(categoryType[category])&&<p>Documentation is unavailable for this asset type.</p>}<div className="inventory-top"><div className="inventory-categories" aria-label="Equipment categories">{categories.map(({id,name,icon})=><button key={id} className="inventory-category" aria-pressed={category===id} onClick={()=>setCategory(current=>current===id?null:id)}><AssetTypeIcon name={icon} size={25}/><strong>{name}</strong><span>{loading?'…':assets.filter(a=>a.category===categoryType[id]).length} assets</span><ChevronRight className="inventory-category-arrow" size={16}/></button>)}</div>
  <aside className="inventory-quick" aria-label="Inventory quick links"><h2>Quick links</h2><button disabled title="Asset scanning will be added after asset registration is available"><ScanLine/><span><strong>Scan asset</strong><small>Coming soon</small></span></button></aside></div>
  <section className="inventory-assets" aria-labelledby="inventory-assets-title"><header><div><h2 id="inventory-assets-title">{filtered?'Assets':'Recently updated equipment'}</h2>{category&&<span className="inventory-filter-chip">{configuration.types.find(t=>t.id===category)?.name}<button aria-label="Clear category" onClick={()=>setCategory(null)}><X size={13}/></button></span>}</div><div className="inventory-list-actions">{permissions.knowledge&&supportedDocumentation(category?categoryType[category]:'Computer')&&<InventoryDocumentationLink category={category?categoryType[category]:'Computer'} computers={assets.filter(a=>a.category===(category?categoryType[category]:'Computer')).length} onOpen={onOpenDocumentation}/>}{(filtered||columnsActive)&&<button className="inventory-clear" onClick={()=>{setSearch('');setCategory(null);setColumnFilters({});}}>Clear filters</button>}</div></header>
   <div className="inventory-table-scroll"><table><thead><tr>{columns.map(([label,key])=><th key={key}><TableColumnFilter label={label} value={columnFilters[key]||''} values={assets.map(a=>cell(a,key))} onChange={value=>setColumnFilters(current=>({...current,[key]:value}))}/></th>)}</tr></thead><tbody>{rows.length&&!loading&&!error?rows.map(a=><tr key={a.id}><td>{a.assetTag}</td><td><button className="inventory-asset-link" aria-label={"Open asset "+a.computer} onClick={()=>setDetails(a)}>{a.computer}</button></td><td>{typeName(configuration.types,a.category,a.assetTypeId)}</td><td>{display(a.customCategory)}</td><td><span className="asset-status">{a.status}</span></td><td>{display(a.assignee)}</td><td>{display(a.department)}</td><td>{display(a.location)}</td><td>{(a.updatedAt?new Date(a.updatedAt).toLocaleDateString():'—')}</td></tr>):<tr><td colSpan={9}><div className="inventory-empty"><Box size={30}/><h3>{loading?'Loading inventory…':error?'Inventory unavailable':(filtered||columnsActive)?'No matching assets.':'No assets yet.'}</h3><p>{error|| (loading?'Please wait.':(filtered||columnsActive)?'Try another search or clear your filters.':'Your equipment will appear here once assets are added.')}</p>{error&&<button onClick={()=>void refresh()}>Retry</button>}</div></td></tr>}</tbody></table></div>
  </section>
  
 {editing!==undefined&&<ComputerAssetEditor reload={async signal=>{const rows=await listAssets(signal);if(!signal.aborted)setAssets(rows);}} assignees={assets.map(a=>a.assignee??'')} categories={assets} asset={editing} close={()=>setEditing(undefined)} saved={()=>{setEditing(undefined);void refresh();}}/>}{details&&<ComputerAssetDetails asset={details} close={()=>setDetails(null)} edit={()=>{setEditing(details);setDetails(null);}}/>}
 </section>;
}


