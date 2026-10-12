"""Freeze legacy prepared-tool constraints for explicit SDK registration; no CAD."""
import json
from pathlib import Path
from mcp_server import ENTITY_SET_TOOLS, OPERATION_PLAN_TOOLS, WORKING_COPY_TOOLS, DOCUMENT_BINDING_TOOLS, OWNED_OPERATION_TOOLS


if __name__ == '__main__':
    tools = [tool for group in (ENTITY_SET_TOOLS, OPERATION_PLAN_TOOLS, WORKING_COPY_TOOLS, DOCUMENT_BINDING_TOOLS, OWNED_OPERATION_TOOLS)
             for tool in group if tool['name'] != 'inspect_entity_candidates']
    folder = Path(__file__).parents[1] / 'src' / 'PowerShapeMcp.Host' / 'Schemas'
    folder.mkdir(exist_ok=True)
    (folder / 'prepared-tools.json').write_text(json.dumps(tools, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Generated %d prepared-tool schemas from retained Python catalog.' % len(tools))
