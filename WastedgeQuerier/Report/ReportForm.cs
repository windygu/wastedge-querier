﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SystemEx.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SourceGrid.Cells;
using SourceGrid.Cells.Controllers;
using SourceGrid.Cells.Views;
using WastedgeApi;
using WastedgeQuerier.JavaScript;
using Cell = SourceGrid.Cells.Cell;

namespace WastedgeQuerier.Report
{
    public partial class ReportForm : SystemEx.Windows.Forms.Form
    {
        private const string DateFormat = "yyyy-MM-dd";
        private const string DateTimeFormat = DateFormat + "'T'HH:mm:ss.fff";
        private const string DateTimeTzFormat = DateTimeFormat + "zzz";

        private readonly Api _api;
        private readonly EntitySchema _entity;
        private readonly List<Filter> _filters;
        private ResultSet _resultSet;
        private readonly List<ResultSet> _resultSets = new List<ResultSet>();
        private ApiQuery _query;
        private SelectedField _selectedField;
        private ReportGridManager _gridManager;

        public ReportForm(Api api, EntitySchema entity, List<Filter> filters)
        {
            if (api == null)
                throw new ArgumentNullException(nameof(api));
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (filters == null)
                throw new ArgumentNullException(nameof(filters));

            _api = api;
            _entity = entity;
            _filters = filters;

            InitializeComponent();

            _gridManager = new ReportGridManager(_grid);

            foreach (ToolStripMenuItem menuItem in _aggregateMenuItem.DropDownItems)
            {
                menuItem.Tag = Enum.Parse(typeof(ReportFieldTransform), (string)menuItem.Tag);
            }

            VisualStyleUtil.StyleTreeView(_fields);

            BuildFields(_fields.Nodes, entity);
        }

        private void BuildFields(TreeNodeCollection nodes, EntitySchema entity)
        {
            nodes.Clear();

            foreach (var member in entity.Members)
            {
                var node = new TreeNode
                {
                    Text = member.Name,
                    Tag = member
                };

                switch (member.Type)
                {
                    case EntityMemberType.Id:
                    case EntityMemberType.Field:
                    case EntityMemberType.Calculated:
                        break;

                    case EntityMemberType.Foreign:
                        node.Nodes.Add("Dummy");
                        break;

                    default:
                        continue;
                }

                nodes.Add(node);
            }
        }

        private void _exportToExcel_Click(object sender, EventArgs e)
        {
            string fileName;

            using (var form = new SaveFileDialog())
            {
                form.Filter = "Excel (*.xlsx)|*.xlsx|All Files (*.*)|*.*";
                form.RestoreDirectory = true;
                form.FileName = "Wastedge Export.xlsx";

                if (form.ShowDialog(this) != DialogResult.OK)
                    return;

                fileName = form.FileName;
            }

            using (var stream = File.Create(fileName))
            {
                new ExcelExporter().Export(stream, _resultSets);
            }

            try
            {
                Process.Start(fileName);
            }
            catch
            {
                // Ignore exceptions.
            }
        }

        private async void _fields_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Tag != null)
                return;

            e.Cancel = true;
            e.Node.Nodes.Clear();

            BuildFields(e.Node.Nodes, await _api.GetEntitySchemaAsync(((EntityForeign)e.Node.Tag).LinkTable));

            e.Node.Expand();
        }

        private void _fields_ItemDrag(object sender, ItemDragEventArgs e)
        {
            var fields = new List<EntityMember>();

            var node = (TreeNode)e.Item;

            while (node != null)
            {
                fields.Insert(0, (EntityMember)node.Tag);
                node = node.Parent;
            }

            DoDragDrop(new ReportField(fields), DragDropEffects.Copy);
        }

        private void _fields_DragOver(object sender, DragEventArgs e)
        {
            var field = (ReportField)e.Data.GetData(typeof(ReportField));
            if (field == null)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = e.AllowedEffect & DragDropEffects.Move;
        }

        private void _fields_DragDrop(object sender, DragEventArgs e)
        {

        }

        private void _fieldsList_ItemClick(object sender, ListBoxItemEventArgs e)
        {
            ShowFieldMenu((ReportFieldListBox)sender, (ReportField)((ReportFieldListBox)sender).Items[e.Index]);
        }

        private void ShowFieldMenu(ReportFieldListBox sender, ReportField reportField)
        {
            _moveToColumnLabelsMenuItem.Enabled = sender.FieldType != ReportFieldType.Column;
            _moveToRowLabelsMenuItem.Enabled = sender.FieldType != ReportFieldType.Row;
            _moveToValuesMenuItem.Enabled = sender.FieldType != ReportFieldType.Value;
            _aggregateMenuItem.Enabled = sender.FieldType == ReportFieldType.Value;

            int index = sender.Items.IndexOf(reportField);

            _moveUpMenuItem.Enabled = index > 0;
            _moveToBeginningMenuItem.Enabled = index > 0;
            _moveDownMenuItem.Enabled = index < sender.Items.Count - 1;
            _moveToEndMenuItem.Enabled = index < sender.Items.Count - 1;

            foreach (ToolStripMenuItem menuItem in _aggregateMenuItem.DropDownItems)
            {
                menuItem.Checked = (ReportFieldTransform)menuItem.Tag == reportField.Transform;
            }

            var bounds = sender.GetItemRectangle(index);

            _selectedField = new SelectedField(sender, reportField);
            _fieldContextMenu.Show(sender, bounds.Left, bounds.Bottom + 1);
        }

        private void _selectAverageMenuItem_Click(object sender, EventArgs e)
        {
            _selectedField.Field.Transform = (ReportFieldTransform)((ToolStripMenuItem)sender).Tag;
            _selectedField.ListBox.UpdateLabels();
        }

        private class SelectedField
        {
            public ReportFieldListBox ListBox { get; }
            public ReportField Field { get; }

            public SelectedField(ReportFieldListBox listBox, ReportField field)
            {
                ListBox = listBox;
                Field = field;
            }
        }

        private void _moveUpMenuItem_Click(object sender, EventArgs e)
        {
            MoveSelectedItem(-1);
        }

        private void _moveDownMenuItem_Click(object sender, EventArgs e)
        {
            MoveSelectedItem(1);
        }

        private void _moveToBeginningMenuItem_Click(object sender, EventArgs e)
        {
            MoveSelectedItem(short.MinValue);
        }

        private void _moveToEndMenuItem_Click(object sender, EventArgs e)
        {
            MoveSelectedItem(short.MaxValue);
        }

        private void MoveSelectedItem(int offset)
        {
            int index = _selectedField.ListBox.Items.IndexOf(_selectedField.Field);
            index = Math.Max(Math.Min(index + offset, _selectedField.ListBox.Items.Count - 1), 0);
            _selectedField.ListBox.Items.Remove(_selectedField.Field);
            _selectedField.ListBox.Items.Insert(index, _selectedField.Field);
        }

        private void _moveToRowLabelsMenuItem_Click(object sender, EventArgs e)
        {
            _selectedField.ListBox.Items.Remove(_selectedField.Field);
            _rows.ForceFieldTransform(_selectedField.Field);
            _rows.Items.Add(_selectedField.Field);
        }

        private void _moveToColumnLabelsMenuItem_Click(object sender, EventArgs e)
        {
            _selectedField.ListBox.Items.Remove(_selectedField.Field);
            _columns.ForceFieldTransform(_selectedField.Field);
            _columns.Items.Add(_selectedField.Field);
        }

        private void _moveToValuesMenuItem_Click(object sender, EventArgs e)
        {
            _selectedField.ListBox.Items.Remove(_selectedField.Field);
            _values.ForceFieldTransform(_selectedField.Field);
            _values.Items.Add(_selectedField.Field);
        }

        private void _removeFieldMenuItem_Click(object sender, EventArgs e)
        {
            _selectedField.ListBox.Items.Remove(_selectedField.Field);
        }

        private async void _update_Click(object sender, EventArgs e)
        {
            if (_columns.Items.Count == 0 || _rows.Items.Count == 0)
            {
                TaskDialogEx.Show(this, "Please select at least one column and row", Text, TaskDialogCommonButtons.OK, TaskDialogIcon.Error);
                return;
            }

            var columns = _columns.Items.Cast<ReportField>().Select(p => p.Clone()).ToList();
            var rows = _rows.Items.Cast<ReportField>().Select(p => p.Clone()).ToList();
            var values = _values.Items.Cast<ReportField>().Select(p => p.Clone()).ToList();

            _gridManager.Reset();

            _update.Enabled = false;

            try
            {
                string response = await _api.ExecuteRawAsync(_entity.Name + "/$report", null, "POST", BuildRequest(columns, rows, values));

                if (IsDisposed)
                    return;

                JObject result;

                using (var reader = new StringReader(response))
                using (var json = new JsonTextReader(reader))
                {
                    json.DateParseHandling = DateParseHandling.None;
                    json.FloatParseHandling = FloatParseHandling.Decimal;

                    result = (JObject)JToken.ReadFrom(json);
                }

                _gridManager.Load(result, columns, rows, values);
            }
            catch (Exception ex)
            {
                TaskDialogEx.Show(this, "An unexpected error occured" + Environment.NewLine + Environment.NewLine + ex.Message, Text, TaskDialogCommonButtons.OK, TaskDialogIcon.Error);
            }
            finally
            {
                _update.Enabled = true;
            }
        }

        private string BuildRequest(List<ReportField> columns, List<ReportField> rows, List<ReportField> values)
        {
            using (var writer = new StringWriter())
            using (var json = new JsonTextWriter(writer))
            {
                json.WriteStartObject();

                json.WritePropertyName("query");
                json.WriteStartObject();

                json.WritePropertyName("fields");
                json.WriteStartArray();
                foreach (var filter in _filters)
                {
                    json.WriteStartObject();

                    json.WritePropertyName("name");
                    json.WriteValue(filter.Field.Name);

                    json.WritePropertyName("op");
                    json.WriteValue(GetFilterTypeCode(filter.Type));

                    if (filter.Value != null)
                    {
                        json.WritePropertyName("value");
                        json.WriteValue(SerializeValue(filter.Value));
                    }
                }
                json.WriteEndArray();

                json.WriteEndObject();

                json.WritePropertyName("rows");
                BuildSimpleFieldsRequest(json, rows);

                json.WritePropertyName("columns");
                BuildSimpleFieldsRequest(json, columns);

                json.WritePropertyName("values");
                json.WriteStartArray();
                foreach (var field in values)
                {
                    json.WriteStartObject();
                    json.WritePropertyName("name");
                    json.WriteValue(GetFieldName(field));
                    json.WritePropertyName("transform");
                    json.WriteValue(GetTransformCode(field.Transform));
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteEndObject();

                return writer.GetStringBuilder().ToString();
            }
        }

        private object GetFilterTypeCode(FilterType type)
        {
            switch (type)
            {
                case FilterType.IsNull:
                    return "is.null";
                case FilterType.NotIsNull:
                    return "not.is.null";
                case FilterType.IsTrue:
                    return "is.true";
                case FilterType.NotIsTrue:
                    return "not.is.true";
                case FilterType.IsFalse:
                    return "is.false";
                case FilterType.NotIsFalse:
                    return "not.is.false";
                case FilterType.In:
                    return "in";
                case FilterType.NotIn:
                    return "not.in";
                case FilterType.Like:
                    return "like";
                case FilterType.NotLike:
                    return "not.like";
                case FilterType.Equal:
                    return "eq";
                case FilterType.NotEqual:
                    return "ne";
                case FilterType.GreaterThan:
                    return "gt";
                case FilterType.GreaterEqual:
                    return "gte";
                case FilterType.LessThan:
                    return "lt";
                case FilterType.LessEqual:
                    return "lte";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private object SerializeValue(object value)
        {
            if (value is DateTime)
                return ((DateTime)value).ToString(DateTimeFormat);
            if (value is DateTimeOffset)
                return ((DateTimeOffset)value).ToString(DateTimeTzFormat);
            return value;
        }

        private string GetTransformCode(ReportFieldTransform transform)
        {
            switch (transform)
            {
                case ReportFieldTransform.Sum:
                    return "sum";
                case ReportFieldTransform.Count:
                    return "count";
                case ReportFieldTransform.Average:
                    return "average";
                case ReportFieldTransform.Max:
                    return "max";
                case ReportFieldTransform.Min:
                    return "min";
                case ReportFieldTransform.Product:
                    return "product";
                case ReportFieldTransform.CountNumbers:
                    return "count-numbers";
                case ReportFieldTransform.StdDev:
                    return "stddev";
                case ReportFieldTransform.StdDevp:
                    return "stddevp";
                case ReportFieldTransform.Var:
                    return "var";
                case ReportFieldTransform.Varp:
                    return "varp";
                default:
                    throw new ArgumentOutOfRangeException(nameof(transform), transform, null);
            }
        }

        private void BuildSimpleFieldsRequest(JsonTextWriter json, IEnumerable<ReportField> fields)
        {
            json.WriteStartArray();
            foreach (var field in fields)
            {
                json.WriteValue(GetFieldName(field));
            }
            json.WriteEndArray();
        }

        private static string GetFieldName(ReportField field)
        {
            return String.Join(".", field.Fields.Select(p => p.Name));
        }
    }
}
