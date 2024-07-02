#!/usr/bin/env ts-node

import * as del from 'del';
import * as fs from 'fs';
// @ts-ignore
import camelCase from 'lodash.camelcase';
// @ts-ignore
import upperFirst from 'lodash.upperfirst';
import * as path from 'path';

interface PropertyInfo {
    name: string;
    type: string;
    attribute?: string;
    defaultValue?: string;
    isEnum: boolean;
    isContent: boolean;
    isAdditionalAttributes: boolean;
}

interface EnumInfo {
    name: string;
    values: string[];
}

interface EnumValue {
    name: string;
    blazorName: string;
}

interface TypeInfo {
    name: string;
    attribute?: string;
    values?: string[];
}

const MJML_DIR = '.';

export interface IMjmlComponent {
    componentName: string;
    allowedAttributes?: Record<string, string>;
    defaultAttributes?: Record<string, string>;
    endingTag?: true;
}

const PRESET_CORE_COMPONENTS: Array<IMjmlComponent> =
    require('mjml-preset-core').components;
const OTHER_SUPPORTED_COMPONENTS = [
    'mjml',
    'mj-all',
    'mj-class',
    'mj-include',
    'mj-html-attribute',
    'mj-selector',
];

const MJML_COMPONENTS_TO_GENERATE = [
    ...OTHER_SUPPORTED_COMPONENTS.map(
        (componentName) => ({ componentName } as IMjmlComponent),
    ),
    ...PRESET_CORE_COMPONENTS,
];

const MJML_COMPONENT_NAMES = MJML_COMPONENTS_TO_GENERATE.map(
    (component) => component.componentName,
);

const TYPE_OVERRIDE: { [componentName: string]: { [prop: string]: string } } = {
    mjml: { owa: 'string', lang: 'string', dir: 'string' },
    'mj-style': { inline: 'enum(inline)' },
    'mj-class': { name: 'string' },
    'mj-selector': { path: 'string' },
    'mj-section': { 'full-width': 'enum(full-width)' },
    'mj-wrapper': { 'full-width': 'enum(full-width)' },
    'mj-html-attribute': { name: 'string' },
    'mj-include': { path: 'string', 'css-inline': 'string', type: 'string' },
};

const HAS_CSS_CLASS = new Set(
    MJML_COMPONENT_NAMES.filter(
        (element) =>
            ![
                'mjml',
                'mj-style',
                'mj-class',
                'mj-breakpoint',
                'mj-include',
                'mj-selector',
                'mj-html-attribute',
            ].includes(element),
    ),
);

const HAS_CHILDREN = new Set(
    MJML_COMPONENT_NAMES.filter(
        (element) =>
            ![
                'mj-all',
                'mj-include',
                'mj-breakpoint',
                'mj-class',
                'mj-divider',
                'mj-image',
                'mj-spacer',
            ].includes(element),
    ),
);

const ALLOW_ANY_PROPERTY = new Set(
    MJML_COMPONENT_NAMES.filter((element) =>
        ['mj-class', 'mj-all'].includes(element),
    ),
);

function buildTypesForComponent(mjmlComponent: IMjmlComponent, blazorName: string)
    : { properties: PropertyInfo[], enums: EnumInfo[] } {
    let {
        componentName,
        allowedAttributes,
        defaultAttributes,
        endingTag: isEndingTag,
    } = mjmlComponent;

    if (!allowedAttributes) {
        allowedAttributes = {};
    }

    const typeOverride = TYPE_OVERRIDE[componentName];
    if (typeOverride !== undefined) {
        for (const [prop, type] of Object.entries(typeOverride)) {
            allowedAttributes[prop] = type;
        }
    }

    const typesFromMjmlAttributes: Record<string, TypeInfo> = {};
    if (allowedAttributes) {
        Object.entries(allowedAttributes).forEach(
            ([mjmlAttribute, mjmlAttributeType]) => {
                const attribute = camelCase(mjmlAttribute);
                const type = getPropTypeFromMjmlAttributeType(
                    attribute,
                    mjmlAttributeType,
                    blazorName,
                );
                const values = mjmlAttributeType.startsWith('enum(')
                    ? transformEnumType(mjmlAttributeType)
                    : undefined;
                typesFromMjmlAttributes[attribute] = {
                    name: type,
                    attribute: mjmlAttribute,
                    values: values,
                };
            },
        );
    }

    if (HAS_CSS_CLASS.has(componentName)) {
        typesFromMjmlAttributes['cssClass'] = { name: 'string?', attribute: 'css-class' };
        typesFromMjmlAttributes['mjmlClass'] = { name: 'string?', attribute: 'mj-class' };
    }
    if (HAS_CHILDREN.has(componentName) || isEndingTag) {
        typesFromMjmlAttributes['childContent'] = { name: 'RenderFragment?' };
    }
    if (ALLOW_ANY_PROPERTY.has(componentName)) {
        typesFromMjmlAttributes['additionalAttributes'] = { name: 'IReadOnlyDictionary<string, object>?' };
    }

    const properties: PropertyInfo[] = [];
    const enums: EnumInfo[] = [];
    Object.entries(typesFromMjmlAttributes).forEach(
        ([attributes, type]) => {
            const defaultValue = defaultAttributes && attributes in defaultAttributes
                ? defaultAttributes[attributes]
                : undefined;
            const property: PropertyInfo = {
                name: attributes,
                type: type.name,
                attribute: type.attribute,
                defaultValue: defaultValue,
                isContent: attributes === 'childContent',
                isEnum: type.name.startsWith('Mjml'),
                isAdditionalAttributes: attributes === 'additionalAttributes',
            };
            if (property.isEnum) {
                enums.push({
                    name: type.name.replace('?', ''),
                    values: type.values!,
                })
            }
            properties.push(property);
        },
    );

    return {
        properties,
        enums,
    };
}

function buildComponentFileContent(
    {
        componentName,
        properties,
        blazorName,
    }: {
        componentName: string;
        properties: PropertyInfo[];
        blazorName: string;
    }): string {
    const parameters = properties
        .sort((a, b) => a.name.localeCompare(b.name))
        .filter(x => !x.isContent && !x.isAdditionalAttributes)
        .map(x => `    [Parameter] public ${x.type} ${upperFirst(x.name)} { get; set; }`)
        .join('\n');
    let index = 0;
    const treeParameters = properties
        .filter(x => !x.isContent && !x.isAdditionalAttributes)
        .map(x => {
            const name = upperFirst(x.name);
            const value = x.isEnum ? `${name}.Value.ToMjmlValue()` : name;
            return `        if (${name} is not null)
            builder.AddAttribute(${++index}, "${x.attribute}", ${value});`;
        })
        .join('\n');
    const hasAdditionalAttributes = properties.some(x => x.isAdditionalAttributes);
    const additionalParameters = hasAdditionalAttributes
        ? `
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }`
        : '';
    const additionalAttributes = hasAdditionalAttributes
        ? `
        if (AdditionalAttributes is not null)
            builder.AddMultipleAttributes(${++index}, AdditionalAttributes);`
        : '';
    const hasChildContent = properties.some(x => x.isContent);
    const childContentParameter = hasChildContent
        ? `
    [Parameter] public RenderFragment? ChildContent { get; set; }`
        : '';
    const childContent = hasChildContent
        ? `
        if (ChildContent is not null)
            builder.AddContent(${++index}, ChildContent);`
        : '';
    const hasEnums = properties.some(x => x.isEnum);
    const enumsImport = hasEnums
        ? `
using ActualChat.Mjml.Blazor.Enums;`
        : '';

    return `// <auto-generated />${enumsImport}
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.Mjml.Blazor.Components;

public class ${blazorName} : ComponentBase
{
${parameters}${childContentParameter}${additionalParameters}

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "${componentName}");
${treeParameters}${additionalAttributes}${childContent}
        builder.CloseElement();
    }
}
`;
}

function generateEnumFileContent(name: string, values: EnumValue[]): string {
    const options = values.map(x => `    ${x.blazorName},`).join('\n');
    return `// <auto-generated />
namespace ActualChat.Mjml.Blazor.Enums;

public enum ${name}
{
${options}
}
`;
}

function generateEnumExtFileContent(name: string, values: EnumValue[]): string {
    const options = values.map(x => `            ${name}.${x.blazorName} => "${x.name}",`).join('\n');
    return `// <auto-generated />
namespace ActualChat.Mjml.Blazor.Enums;

public static class ${name}Ext
{
    public static string ToMjmlValue(this ${name} value)
        => value switch {
${options}
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
}
`;
}

export function getPropTypeFromMjmlAttributeType(
    attribute: string,
    mjmlAttributeType: string,
    blazorName: string,
): string {
    if (mjmlAttributeType === 'boolean') {
        return 'bool?';
    }
    if (mjmlAttributeType === 'integer') {
        return 'int?';
    }
    // e.g. "vertical-align": "enum(top,bottom,middle)"
    if (mjmlAttributeType.startsWith('enum(')) {
        return `${blazorName}${upperFirst(attribute)}?`;
    }
    return 'string?';
}

function transformEnumType(mjmlAttributeType: string): string[] | undefined {
    return mjmlAttributeType
        .match(/\(.*\)/)?.[0]
        ?.slice(1, -1)
        .split(',');
}

// reset directory
const GENERATED_MJML_FILES = path.join(MJML_DIR, '../dotnet/Mjml.Blazor');
const GENERATED_MJML_COMPONENT_FILES = path.join(GENERATED_MJML_FILES, 'Components');
const GENERATED_MJML_ENUM_FILES = path.join(GENERATED_MJML_FILES, 'Enums');
del.sync(GENERATED_MJML_COMPONENT_FILES, { force: true });
del.sync(GENERATED_MJML_ENUM_FILES, { force: true });
fs.mkdirSync(GENERATED_MJML_COMPONENT_FILES);
fs.mkdirSync(GENERATED_MJML_ENUM_FILES);

// create components
const allEnums: { [id: string]: string[] } = {};
for (const mjmlComponent of MJML_COMPONENTS_TO_GENERATE) {
    const { componentName } = mjmlComponent;
    const mjmlPackageName = componentName.replace('mj-', 'mjml-');
    const blazorName = upperFirst(camelCase(mjmlPackageName));

    const { properties, enums } = buildTypesForComponent(mjmlComponent, blazorName);
    enums.forEach(x => allEnums[`${x.name}`] ??= x.values );
    const fileContent = buildComponentFileContent({ componentName, properties, blazorName });
    fs.writeFileSync(
        path.join(GENERATED_MJML_COMPONENT_FILES, `${blazorName}.cs`),
        fileContent,
    );
}

for (const enumName of Object.keys(allEnums)) {
    var values = allEnums[enumName]!
        .map(name => {
        return {
            name: name,
            blazorName: upperFirst(camelCase(name)),
        };
    });

    const enumFileContent = generateEnumFileContent(enumName, values);
    fs.writeFileSync(
        path.join(GENERATED_MJML_ENUM_FILES, `${enumName}.cs`),
        enumFileContent,
    );

    const enumFileExtContent = generateEnumExtFileContent(enumName, values);
    fs.writeFileSync(
        path.join(GENERATED_MJML_ENUM_FILES, `${enumName}Ext.cs`),
        enumFileExtContent,
    );
}
