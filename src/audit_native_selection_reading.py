"""Independent result/guard audit for passive native selection decoding."""
import argparse
import json
from pathlib import Path

FAULTS={'control','empty','reordered','lower_bound','null_inventory','null_selection','scalar_selection',
        'wrong_id_type','multi_rank','duplicate','foreign','selection_drift','inventory_drift','wrong_metadata',
        'count_mismatch','null_payload','wrong_count','document','windows','retained','expired'}


def audit(stage):
    report=json.loads((stage/'reports/native-selection-reading-tests.json').read_text(encoding='utf-8-sig'))
    assert (stage/'.native-selection-fixture').is_file()
    assert report['state']=='passed' and report['cadConnectionAttempted'] is False
    assert len(report['rows'])==len(FAULTS) and {r['fault'] for r in report['rows']}==FAULTS
    for row in report['rows']:
        result=row['result']
        for flag in ('contextChangeAttempted','geometryMutationAttempted','cadSaveAttempted','nativeLayoutQualified'):
            assert result[flag] is False
        control=row['fault'] in {'control','empty','reordered','lower_bound'}
        assert result['state']==('native_selection_items_read_unqualified' if control else 'native_selection_read_rejected')
        assert row['cadConnectionAttempted'] is False
        if control:
            assert row['selectionReads']==row['modelReads']==row['observations']==2
            items=result['selectedItemsCanonicalOrder']
            assert [i['compositeId'] for i in items]==([] if row['fault']=='empty' else [21,40])
            assert result['selectedEntityKeys']==([] if row['fault']=='empty' else [[21,8],[40,5]])
            assert result['selectedCompositeIdsReportedOrder']==([] if row['fault']=='empty' else [40,21])
            assert result['selectionCount']==len(items)
            for item in items:
                assert item['name']=='Synthetic_'+str(item['nativeTypeCode']) and item['levelNumber']==0
    return dict(state='passed',cases=len(FAULTS),controls=4,refusals=17,cadConnectionAttempted=False,
                nativeLayoutQualified=False)


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--stage',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    result=audit(args.stage.resolve())
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('PASS: 21 passive selection cases, 4 controls/17 refusals; no CAD.')
