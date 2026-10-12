from group_job import dgk_filename, groups_by_source_level
assert dgk_filename('1:Vneshnie 0.6')=='1-Vneshnie 0.6.dgk'
rows=[{'sourceLevel':'1:Vneshnie 0.6','zWorldMm':z} for z in (0,.6,1.2)]
rows+=[{'sourceLevel':'2:Vnutrennie 0.8','zWorldMm':z} for z in (0,.8)]
groups=groups_by_source_level(rows)
assert len(groups)==2 and len(groups[0]['contours'])==3 and len(groups[1]['contours'])==2
assert [c['zWorldMm'] for c in groups[0]['contours']]==[0,.6,1.2]
for bad in ('../escape','a/b','a\\b','a?b','a\nb','CON','LPT1','CON .test','.', '..',' name','name ','name.','a\'b'):
    try:dgk_filename(bad)
    except ValueError:pass
    else:raise AssertionError(bad)
for duplicate in (('1:a','1-a'),('1:A','1:a')):
    try:groups_by_source_level([{'sourceLevel':s} for s in duplicate])
    except ValueError:pass
    else:raise AssertionError(duplicate)
print('PASS: source-level grouping across heights, exact user filename, spaces, invalid names and Windows filename collisions.')
