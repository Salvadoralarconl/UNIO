import {useEffect,useState,type ReactNode} from 'react';
import {createPortal} from 'react-dom';
export default function InventoryToolbarActions({children}:{children:ReactNode}){const [target,setTarget]=useState<HTMLElement|null>(null);useEffect(()=>{setTarget(document.getElementById('inventory-toolbar-actions'));},[]);const actions=<div className="inventory-toolbar-actions">{children}</div>;return target?createPortal(actions,target):actions;}
