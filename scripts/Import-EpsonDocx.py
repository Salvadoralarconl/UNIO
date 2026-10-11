"""Import the complete user DOCX as one knowledge-base revision."""
from pathlib import Path
from docx import Document
import hashlib, json, shutil, sys
root=Path(__file__).resolve().parents[1]
source=Path(sys.argv[1])
originals=root/'docs'/'epson'/'originals'
originals.mkdir(parents=True,exist_ok=True)
shutil.copy2(source,originals/source.name)
document=Document(source)
if document.tables: raise ValueError('Table extraction is required before importing a document with tables.')
paragraphs=[p.text for p in document.paragraphs]
text='\n\n'.join(paragraphs)
text+='\n\n## Import provenance\nSource: Epson Receipt Printer Troubleshooting.docx. Entire document, paragraph order and wording preserved.\nUser-provided USB/APD procedure; not an official Epson publication.\nOriginal SHA-256: '+hashlib.sha256(source.read_bytes()).hexdigest()+'\n'
(root/'samples'/'epson-receipt-printer-troubleshooting.md').write_text(text,encoding='utf-8')
manifest_path=root/'samples'/'manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest=[d for d in manifest if not d['id'].startswith('30000000-0000-0000-0000-')]
manifest.append(dict(id='30000000-0000-0000-0000-000000000001',title='Epson USB/APD — User-provided — Epson Receipt Printer Troubleshooting',version='Imported snapshot 2026-10-06',file='epson-receipt-printer-troubleshooting.md',approval='approved'))
manifest_path.write_text(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
print(f'Imported one document with {len(paragraphs)} original paragraphs.')
