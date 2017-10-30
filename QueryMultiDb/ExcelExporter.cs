﻿using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
// ReSharper disable PossiblyMistakenUseOfParamsMethod

namespace QueryMultiDb
{
    public static class ExcelExporter
    {
        public static void Generate(ICollection<Table> tables)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables), "Parameter cannot be null.");
            }

            var destination = Parameters.Instance.OutputDirectory + @"\" + Parameters.Instance.OutputFile;

            using (var spreadSheet = SpreadsheetDocument.Create(destination, SpreadsheetDocumentType.Workbook))
            {
                Logger.Instance.Info($"Created excel file {destination}");
                var workbookPart = spreadSheet.AddWorkbookPart();

                spreadSheet.WorkbookPart.Workbook = new Workbook
                {
                    Sheets = new Sheets()
                };

                foreach (var table in tables)
                {
                    Logger.Instance.Info("Adding new excel sheet.");
                    AddSheet(spreadSheet, table);
                }

                var logTable = LogsToTable(Logger.Instance.Logs);
                AddSheet(spreadSheet, logTable, "Logs");

                var parameterTable = ParametersToTable(Parameters.Instance);
                AddSheet(spreadSheet, parameterTable, "Parameters");
            }

            Logger.Instance.Info("Excel file closed after generation.");
        }

        private static Table ParametersToTable(Parameters parameters)
        {
            var parameterColumns = new TableColumn[2];
            parameterColumns[0] = new TableColumn("Parameter", typeof(string));
            parameterColumns[1] = new TableColumn("Value", typeof(string));

            var parameterRows = new List<TableRow>
            {
                CreateParameterRow("OutputDirectory", parameters.OutputDirectory),
                CreateParameterRow("OutputFile", parameters.OutputFile),
                CreateParameterRow("Overwrite", parameters.Overwrite),
                CreateParameterRow("Targets", Parameters.TargetsToJsonString(parameters.Targets)),
                CreateParameterRow("Query", parameters.Query),
                CreateParameterRow("Debug", parameters.Debug.ToString()),
                CreateParameterRow("ConnectionTimeout", parameters.ConnectionTimeout),
                CreateParameterRow("CommandTimeout", parameters.CommandTimeout),
                CreateParameterRow("Sequential", parameters.Sequential),
                CreateParameterRow("Parallelism", parameters.Parallelism),
                CreateParameterRow("IncludeIP", parameters.IncludeIP),
                CreateParameterRow("Quiet", parameters.Quiet)
            };

            var parameterTable = new Table(parameterColumns, parameterRows);

            return parameterTable;
        }

        private static TableRow CreateParameterRow(string parameter, object value)
        {
            var items = new object[2];
            items[0] = parameter;
            items[1] = value;
            var tableRow = new TableRow(items);
            return tableRow;
        }

        private static Table LogsToTable(ICollection<Log> logs)
        {
            var logColumns = new TableColumn[8];
            logColumns[0] = new TableColumn("Id", typeof(int));
            logColumns[1] = new TableColumn("Date", typeof(string));
            logColumns[2] = new TableColumn("Server", typeof(string));
            logColumns[3] = new TableColumn("Database", typeof(string));
            logColumns[4] = new TableColumn("ThreadId", typeof(int));
            logColumns[5] = new TableColumn("Level", typeof(string));
            logColumns[6] = new TableColumn("Message", typeof(string));
            logColumns[7] = new TableColumn("Exception", typeof(string));

            var logRows = new List<TableRow>(logs.Count);

            foreach (var log in logs)
            {
                var items = new object[8];
                items[0] = log.Id;
                items[1] = log.Date.ToString("o");
                items[2] = log.Server ?? "";
                items[3] = log.Database ?? "";
                items[4] = log.ThreadId;
                items[5] = log.Level;
                items[6] = log.Message;
                items[7] = log.Exception?.ToString() ?? "";
                var tableRow = new TableRow(items);
                logRows.Add(tableRow);
            }

            var logTable = new Table(logColumns, logRows);

            return logTable;
        }

        private static void AddSheet(SpreadsheetDocument spreadSheet, Table table, string sheetName = null)
        {
            var sheetPart = spreadSheet.WorkbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            sheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = spreadSheet.WorkbookPart.Workbook.GetFirstChild<Sheets>();
            var relationshipId = spreadSheet.WorkbookPart.GetIdOfPart(sheetPart);

            uint sheetId = 1;

            if (sheets.Elements<Sheet>().Any())
            {
                sheetId = sheets.Elements<Sheet>().Select(s => s.SheetId.Value).Max() + 1;
            }

            var sheet = new Sheet()
            {
                Id = relationshipId,
                SheetId = sheetId,
                Name = sheetName ?? "Sheet" + sheetId
            };
            sheets.Append(sheet);

            var headerRow = new Row();

            foreach (var column in table.Columns)
            {
                var cell = new Cell
                {
                    DataType = CellValues.String,
                    CellValue = new CellValue(column.ColumnName)
                };
                headerRow.AppendChild(cell);
            }

            sheetData.AppendChild(headerRow);

            foreach (var tableRow in table.Rows)
            {
                var newRow = new Row();

                for (var columnIndex = 0; columnIndex < table.Columns.Length; columnIndex++)
                {
                    var cell = new Cell
                    {
                        DataType = GetExcelCellDataType(tableRow.ItemArray[columnIndex].GetType()),
                        CellValue = new CellValue(GetExcelCellString(tableRow.ItemArray[columnIndex]))
                    };

                    newRow.AppendChild(cell);
                }

                sheetData.AppendChild(newRow);
            }

            var tableDefinitionPart = sheetPart.AddNewPart<TableDefinitionPart>();
            GenerateTablePartContent(tableDefinitionPart, table.Columns, table.Rows.Count, spreadSheet.WorkbookPart);
            var tableParts1 = new TableParts {Count = 1U};
            var tablePart1 = new TablePart {Id = sheetPart.GetIdOfPart(tableDefinitionPart)};
            tableParts1.Append(tablePart1);
            sheetPart.Worksheet.Append(tableParts1);
        }

        private static void GenerateTablePartContent(TableDefinitionPart part, TableColumn[] columns, int rowCount, WorkbookPart workBookPart)
        {
            var rangeReference = GetXlsRangeReference(0, 1, columns.Length - 1, rowCount + 1);
            var autoFilter = new AutoFilter {Reference = rangeReference};
            var tableColumns = new TableColumns {Count = (uint) columns.Length};
            var styleInfo = new TableStyleInfo
            {
                Name = "TableStyleMedium2",
                ShowFirstColumn = false,
                ShowLastColumn = false,
                ShowRowStripes = true,
                ShowColumnStripes = false
            };

            var tableId = workBookPart.WorksheetParts
                              .Select(x => x.TableDefinitionParts.Where(y => y.Table != null)
                                  .Select(y => (uint) y.Table.Id).DefaultIfEmpty(0U).Max()).DefaultIfEmpty(0U).Max() + 1;

            var table =
                new DocumentFormat.OpenXml.Spreadsheet.Table(autoFilter, tableColumns, styleInfo)
                {
                    Id = tableId,
                    Name = "Table" + tableId,
                    DisplayName = "Table" + tableId,
                    Reference = rangeReference,
                    TotalsRowShown = false
                };

            for (var i = 0; i < columns.Length; i++)
            {
                table.TableColumns.Append(
                    new DocumentFormat.OpenXml.Spreadsheet.TableColumn
                    {
                        Id = (uint) (i + 1),
                        Name = columns[i].ColumnName
                    });
            }

            part.Table = table;
        }

        private static string GetXlsRangeReference(int col1, int row1, int col2, int row2)
        {
            var x = GetXlsCellReference(Math.Min(col1, col2), Math.Min(row1, row2));
            var y = GetXlsCellReference(Math.Max(col1, col2), Math.Max(row1, row2));

            return $"{x}:{y}";
        }

        private static string GetXlsCellReference(int col, int row)
        {
            return $"{GetXlsColumnReference(col)}{row}";
        }

        private static string GetXlsColumnReference(int colIndex)
        {
            var r = string.Empty;

            do
            {
                r = (char) ((byte) 'A' + colIndex % 26) + r;
            } while ((colIndex = colIndex / 26 - 1) >= 0);

            return r;
        }

        private static EnumValue<CellValues> GetExcelCellDataType(Type dataType)
        {
            if (dataType == typeof(bool))
            {
                // CellValues.String and not CellValues.Boolean because we ouput the boolean as a string value.
                return CellValues.String;
            }

            if (dataType == typeof(int) || dataType == typeof(long) || dataType == typeof(short))
            {
                return CellValues.Number; 
            }

            if (dataType == typeof(DateTime))
            {
                // Works in Office 2010+ with "s" formated datetime.
                return CellValues.Date;
            }

            return CellValues.String;
        }

        private static string GetExcelCellString(object tableRowItem)
        {
            if (tableRowItem is DateTime)
            {
                var dateTime = (DateTime) tableRowItem;

                return dateTime.ToString("s");
            }

            return tableRowItem.ToString();
        }
    }
}