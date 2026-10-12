"""Independent execution facade parity through SDK STDIO and fake panel; no CAD."""
import argparse
from test_sdk_prepared import checks

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--host', required=True)
    parser.add_argument('--evidence', required=True)
    args = parser.parse_args()
    args.execution = True
    checks(args)
