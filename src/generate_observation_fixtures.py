"""Generate synthetic golden observations using the retained Python policy; no CAD."""
import copy
import json
from pathlib import Path
from entity_references import identity, inventory, observation_digest, digest
from test_entity_references import snapshot


def generate():
    cases = []
    base = snapshot()
    variations = [('baseline', base)]
    for index, path in enumerate(['', 'D:/Parts/./folder/../part.psmodel', 'D:Parts/../Part.psmodel',
                                  r'\\server\share\folder\..\Part.psmodel', r'\Parts\..\Part.psmodel']):
        value = copy.deepcopy(base)
        value['model']['path'] = path
        variations.append(('path-%d' % index, value))
    for index, number in enumerate([1.0, -0.0, 1e-6, 1e16, 1e15, 1.2345e-7, 0.0001, 1.2345e20]):
        value = copy.deepcopy(base)
        value['entities'][0]['geometry']['volumeRaw'] = number
        variations.append(('float-%d' % index, value))
    value = copy.deepcopy(base)
    value['entities'][0]['name'] = 'Тело 😀\u2028\u2029\t"\\'
    value['entities'][0]['geometry'].update({'😀': 2, '\ue000': 3, 'a': 4})
    variations.append(('unicode-keys-and-values', value))
    value = copy.deepcopy(base)
    value['entities'] = []
    value['entityCount'] = 0
    variations.append(('empty-inventory', value))
    value = copy.deepcopy(base)
    value['entities'].append({**value['entities'][0], 'nativeId': 4, 'name': 'Second'})
    value['entityCount'] = 2
    variations.append(('unsorted-inventory', value))
    for name, value in variations:
        model = identity(value)
        cases.append({'name': name, 'snapshot': value, 'identity': model, 'identitySha256': digest(model),
                      'observedPropertiesSha256': observation_digest(value, inventory(value))})
    target = Path(__file__).parents[1] / 'tests' / 'PowerShapeMcp.Tests' / 'fixtures' / 'observations.json'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(cases, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Generated %d synthetic legacy-policy golden observations.' % len(cases))


if __name__ == '__main__':
    generate()
