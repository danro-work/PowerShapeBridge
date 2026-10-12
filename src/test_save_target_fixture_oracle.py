"""Type-sensitive target artifact oracle regression; no CAD/queue/files."""
import unittest
from save_target_fixture_oracle import same_json


class JsonTypes(unittest.TestCase):
    def test_boolean_is_not_integer_in_nested_proof(self):
        self.assertFalse(same_json({'target': {'schemaVersion': 1}}, {'target': {'schemaVersion': True}}))

    def test_false_is_not_zero_in_nested_array(self):
        self.assertFalse(same_json({'rows': [False]}, {'rows': [0]}))

    def test_number_value_equivalence_is_preserved(self):
        self.assertTrue(same_json({'vector': [1.0, 0, 0]}, {'vector': [1, 0.0, 0]}))

    def test_type_correct_identical_proof_is_equal(self):
        self.assertTrue(same_json({'enabled': False, 'schemaVersion': 1}, {'enabled': False, 'schemaVersion': 1}))


if __name__ == '__main__':
    unittest.main()
