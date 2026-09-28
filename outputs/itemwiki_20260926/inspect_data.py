import analyze as a
import re
from lxml import etree as E
from collections import Counter
print('COMPS')
for dn,i in a.items.items():
 n=E.fromstring(i['xml']); cs=n.xpath('comps/li[@Class]')
 custom=[c for c in cs if c.get('Class').startswith('Helodrace')]
 if custom:print(dn, ' | '.join(E.tostring(c,encoding='unicode') for c in custom))
print('OTHER EQUIPMENT CANDIDATES')
for dn,n in a.modthings.items():
 if dn not in a.items and dn not in a.excluded and n.findtext('category')=='Item':
  if n.find('tools') is not None or n.find('verbs') is not None or re.search('explos|breach|bomb|grenade|rocket|weapon',n.findtext('label',''),re.I):print(dn,E.tostring(n,encoding='unicode'))
print('MATERIAL ITEMS')
for dn,i in a.items.items():
 n=E.fromstring(i['xml'])
 if n.find('stuffCategories') is not None:print(dn,n.xpath('stuffCategories/li/text()'),n.findtext('costStuffCount'))
