import {test,expect} from './session-fixture';
import {calculateGrid} from '../src/tableMath';
test('safe formulas aggregate numeric cells and respect arithmetic precedence',()=>{const grid=[['10','20','note'],['=SUM(A1:C1)','=AVERAGE(A1:B1)','=COUNT(A1:C1)'],['=MIN(A1:B1)','=MAX(A1:B1)','=A1+B1*2'],['=SUM(A1:B1)/2','=SUM(A1, B1, 5)','=-(A1+2)']];expect(calculateGrid(grid)).toEqual([['10','20','note'],['30','15','2'],['10','20','50'],['15','35','-12']]);});
test('formulas report cycles invalid references and division errors without evaluating code',()=>{expect(calculateGrid([['=B1','=A1','=1/0','=Z99','=alert(1)','=SUM(A1:A999999)']])[0]).toEqual(['#CYCLE!','#CYCLE!','#DIV/0!','#REF!','#ERROR!','#REF!']);});

