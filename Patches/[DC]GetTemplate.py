import xml.etree.ElementTree as ET
import os
import sys

def generate_correct_trait_template(input_xml_path):
    if not os.path.exists(input_xml_path):
        print(f"Error: File not found: {input_xml_path}")
        return

    try:
        tree = ET.parse(input_xml_path)
        root = tree.getroot()
    except ET.ParseError as e:
        print(f"Error parsing XML: {e}")
        return

    output_lines = []
    output_lines.append('<?xml version="1.0" encoding="utf-8" ?>')
    output_lines.append('<Patch>')
    output_lines.append('')
    
    count = 0

    for trait_def in root.findall('TraitDef'):
        def_name = trait_def.find('defName').text
        degree_datas = trait_def.find('degreeDatas')
        
        output_lines.append(f'  <!-- Trait: {def_name} -->')
        output_lines.append('  <Operation Class="PatchOperationAdd">')
        output_lines.append(f'    <xpath>Defs/TraitDef[defName="{def_name}"]</xpath>')
        output_lines.append('    <value>')
        output_lines.append('      <modExtensions>')
        output_lines.append('        <li Class="RimLife.PersonalityExtension">')
        output_lines.append('          <data>')

        # === 逻辑修正：在一个 Operation 内循环 Degree ===
        if degree_datas is not None:
            for degree_data in degree_datas.findall('li'):
                label = degree_data.find('label').text if degree_data.find('label') is not None else "Unknown"
                degree = degree_data.find('degree').text if degree_data.find('degree') is not None else "0"
                
                output_lines.append(f'            <!-- Label: {label} -->')
                output_lines.append('            <li>')
                output_lines.append(f'              <degree>{degree}</degree>')
                output_lines.append('              <openness>0</openness>')
                output_lines.append('              <conscientiousness>0</conscientiousness>')
                output_lines.append('              <extraversion>0</extraversion>')
                output_lines.append('              <agreeableness>0</agreeableness>')
                output_lines.append('              <neuroticism>0</neuroticism>')
                output_lines.append('            </li>')
        else:
            # 单一特质
            output_lines.append('            <!-- Singular Trait -->')
            output_lines.append('            <li>')
            output_lines.append('              <degree>0</degree>')
            output_lines.append('              <openness>0</openness>')
            output_lines.append('              <conscientiousness>0</conscientiousness>')
            output_lines.append('              <extraversion>0</extraversion>')
            output_lines.append('              <agreeableness>0</agreeableness>')
            output_lines.append('              <neuroticism>0</neuroticism>')
            output_lines.append('            </li>')

        output_lines.append('          </data>')
        output_lines.append('        </li>')
        output_lines.append('      </modExtensions>')
        output_lines.append('    </value>')
        output_lines.append('  </Operation>')
        output_lines.append('')
        count += 1

    output_lines.append('</Patch>')

    # 输出
    base_name = os.path.splitext(input_xml_path)[0]
    output_path = f"{base_name}_CorrectedPatch.xml"
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(output_lines))
    print(f"Generated corrected template: {output_path}")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python script.py <xml_path>")
    else:
        generate_correct_trait_template(sys.argv[1])
