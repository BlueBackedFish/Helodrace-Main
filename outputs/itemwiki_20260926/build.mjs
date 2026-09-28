import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {Workbook,SpreadsheetFile} from '@oai/artifact-tool';
const dir=path.dirname(fileURLToPath(import.meta.url));
const input=JSON.parse(await fs.readFile(path.join(dir,'rows.json'),'utf8'));
const wb=Workbook.create();
const col=n=>{let s='';while(n){n--;s=String.fromCharCode(65+n%26)+s;n=Math.floor(n/26);}return s;};
const specs={
 Weapons:{widths:[32,19,11,12,14,16,11,17,14,17,17,17,17,13,13,14,17,11,20,14,58,72],percent:[4,10,11,12,13],text:[1,2,19,20,21,22],previews:['A1:I9','J1:V6','A23:I31','S33:V43']},
 Apparel:{widths:[34,20,40,13,13,13,14,22,18,18,13,21,21,13,14,14,14,25,64,14,76],percent:[4,5,6,12],text:[1,2,3,19,20,21],previews:['A1:K9','L1:U7','A30:N38','O23:U31']},
};
const summary={};
for(const [name,data] of Object.entries(input)){
 const s=wb.worksheets.add(name);const spec=specs[name];const n=data.rows.length+1;const end=col(data.headers.length);
 const all=s.getRange(`A1:${end}${n}`);
 all.values=[data.headers,...data.rows];
 all.format.font={name:'Malgun Gothic',size:10,color:'#202B36'};
 all.format.wrapText=true;
 all.format.verticalAlignment='center';
 all.format.rowHeight=34;
 all.setNumberFormat('0.##');
 for(let c=1;c<=data.headers.length;c++){
  const letter=col(c);const rg=s.getRange(`${letter}1:${letter}${n}`);
  rg.format.columnWidth=spec.widths[c-1];
  rg.format.horizontalAlignment=spec.text.includes(c)?'left':'right';
  if(spec.percent.includes(c))s.getRange(`${letter}2:${letter}${n}`).setNumberFormat('0.#%');
 }
 const priceCol=name==='Weapons'?'O':'K';s.getRange(`${priceCol}2:${priceCol}${n}`).setNumberFormat('#,##0.##');
 const table=s.tables.add(`A1:${end}${n}`,true,`${name}Items`);table.style='TableStyleLight9';table.showFilterButton=true;
 const header=s.getRange(`A1:${end}1`);
 header.format={fill:'#2D4053',font:{name:'Malgun Gothic',size:10,bold:true,color:'#FFFFFF'},horizontalAlignment:'center',verticalAlignment:'center',wrapText:true,rowHeight:46,borders:{insideVertical:{style:'thin',color:'#FFFFFF'}}};
 function displayWidth(value){return [...String(value??'')].reduce((a,x)=>a+(x.charCodeAt(0)>255?1.65:1),0);}
 for(let r=0;r<data.rows.length;r++){
  let lines=1;
  for(let c=0;c<data.headers.length;c++){
   const value=data.rows[r][c];
   if(typeof value==='number'){
    let format;
    if(spec.percent.includes(c+1))format=Number.isInteger(Math.round(value*1e8)/1e6)?'0%':'0.0%';
    else if(Number.isInteger(value))format=(c+1===(name==='Weapons'?15:11))?'#,##0':'0';
    else if(name==='Apparel'&&c+1===8)format='0.###';
    if(format)s.getRange(`${col(c+1)}${r+2}`).setNumberFormat(format);
   }
   if(typeof value!=='string')continue;
   const num=value.split('\n').reduce((sum,line)=>sum+Math.max(1,Math.ceil(displayWidth(line)/(spec.widths[c]-2))),0);
   lines=Math.max(lines,num);
  }
  s.getRange(`A${r+2}:${end}${r+2}`).format.rowHeight=Math.max(34,lines*15+10);
  if(r===0||data.rows[r][1]!==data.rows[r-1][1]){
   s.getRange(`A${r+2}:${end}${r+2}`).format.borders={top:{style:'thin',color:'#A4B4C3'}};
   s.getRange(`A${r+2}:B${r+2}`).format.font.bold=true;
  }
 }
 s.showGridLines=false;s.freezePanes.freezeRows(1);s.freezePanes.freezeColumns(2);
 const noteStart=n+3;
 data.notes.forEach((note,idx)=>{
  const r=noteStart+idx;
  s.getRange(`A${r}`).values=[[note]];
  s.getRange(`A${r}:${name==='Weapons'?'M':'L'}${r}`).merge();
  const rg=s.getRange(`A${r}:${name==='Weapons'?'M':'L'}${r}`);
  rg.format={font:{name:'Malgun Gothic',size:10,color:'#536271'},wrapText:true,rowHeight:29,verticalAlignment:'center'};
 });
 summary[name]={rows:data.rows.length,range:`A1:${end}${n}`,table:table.name};
 const result=await wb.inspect({kind:'table',range:`${name}!A1:H5`,include:'values',tableMaxRows:5,tableMaxCols:8,maxChars:3000});
 await fs.writeFile(path.join(dir,`${name}_inspect.ndjson`),result.ndjson);
 for(let i=0;i<spec.previews.length;i++){
  const preview=await wb.render({sheetName:name,range:spec.previews[i],scale:1.3,format:'png'});
  await fs.writeFile(path.join(dir,`${name}_preview_${i+1}.png`),new Uint8Array(await preview.arrayBuffer()));
 }
}
const errors=await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!',options:{useRegex:true,maxResults:20},summary:'final error scan',maxChars:1500});
await fs.writeFile(path.join(dir,'error_scan.ndjson'),errors.ndjson);
const filename=path.join(dir,'Helodrace_무기_의상_비교표.xlsx');
const xlsx=await SpreadsheetFile.exportXlsx(wb);await xlsx.save(filename);
await fs.writeFile(path.join(dir,'workbook_summary.json'),JSON.stringify(summary,null,2));
console.log(JSON.stringify({filename,summary,errorScan:errors.ndjson}));
