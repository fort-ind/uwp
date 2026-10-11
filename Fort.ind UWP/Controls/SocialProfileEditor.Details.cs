using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Fort.ind_UWP
{
    public sealed partial class SocialProfileEditor
    {
        private const double FieldRowsStackBelowWidth = 400;

        private readonly ObservableCollection<SocialProfileFieldRow> _fieldRows = new ObservableCollection<SocialProfileFieldRow>();

        private string _savedLocation = "";

        private string _savedBirthday;

        private IReadOnlyList<SocialUserField> _savedFields = new List<SocialUserField>();

        private bool _fieldRowsStacked;

        private bool LocationChanged
        {
            get { return !string.Equals(Normalize(LocationBox.Text), Normalize(_savedLocation), StringComparison.Ordinal); }
        }

        private string CurrentBirthday
        {
            get { return SocialProfileEditService.FormatBirthday(BirthdayPicker.Date); }
        }

        private bool BirthdayChanged
        {
            get { return !string.Equals(CurrentBirthday, _savedBirthday, StringComparison.Ordinal); }
        }

        private IReadOnlyList<SocialUserField> CurrentFields
        {
            get { return SocialProfileEditService.CleanFields(_fieldRows.Select(row => new SocialUserField(row.Name, row.Value))); }
        }

        private bool FieldsChanged
        {
            get { return !SameFields(CurrentFields, _savedFields); }
        }

        private bool DetailsChanged
        {
            get { return LocationChanged || BirthdayChanged || FieldsChanged; }
        }

        private bool DetailsValid
        {
            get { return SocialProfileEditService.Length(LocationBox.Text) <= SocialProfileEditService.LocationMaxLength && _fieldRows.All(row => !row.HasError); }
        }

        private static bool SameFields(IReadOnlyList<SocialUserField> a, IReadOnlyList<SocialUserField> b)
        {
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)) return false;
                if (!string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal)) return false;
            }

            return true;
        }

        private void ResetDetails(string location, string birthday, IReadOnlyList<SocialUserField> fields)
        {
            _savedLocation = location ?? "";
            _savedBirthday = NormalizeBirthday(birthday);
            _savedFields = SocialProfileEditService.CleanFields(fields);

            SetText(LocationBox, _savedLocation);
            SetBirthday(_savedBirthday);
            SetFieldRows(_savedFields);
        }

        private void RebaseDetails(string location, string birthday, IReadOnlyList<SocialUserField> fields)
        {
            RebaseField(LocationBox, ref _savedLocation, location ?? "");

            var newBirthday = NormalizeBirthday(birthday);
            var birthdayUntouched = !BirthdayChanged;
            _savedBirthday = newBirthday;
            if (birthdayUntouched) SetBirthday(newBirthday);

            var newFields = SocialProfileEditService.CleanFields(fields);
            var fieldsUntouched = !FieldsChanged;
            _savedFields = newFields;
            if (fieldsUntouched && !SameFields(CurrentFields, newFields)) SetFieldRows(newFields);
        }

        private void RestoreSavedDetails()
        {
            SetText(LocationBox, _savedLocation);
            SetBirthday(_savedBirthday);
            SetFieldRows(_savedFields);
        }

        private void AddDetailsTo(SocialProfileEdit edit)
        {
            edit.LocationChanged = LocationChanged;
            edit.Location = LocationBox.Text;
            edit.BirthdayChanged = BirthdayChanged;
            edit.Birthday = CurrentBirthday;
            edit.FieldsChanged = FieldsChanged;
            edit.Fields = CurrentFields;
        }

        private void AcceptSavedDetails(SocialProfileEdit edit)
        {
            _savedLocation = edit.Location ?? "";
            _savedBirthday = edit.Birthday;
            _savedFields = SocialProfileEditService.CleanFields(edit.Fields);
        }

        private static string NormalizeBirthday(string birthday)
        {
            return SocialProfileEditService.FormatBirthday(SocialProfileEditService.ParseBirthday(birthday));
        }

        private void SetBirthday(string birthday)
        {
            _loading = true;
            try
            {
                BirthdayPicker.Date = SocialProfileEditService.ParseBirthday(birthday);
            }
            finally
            {
                _loading = false;
            }

            ClearBirthdayButton.Visibility = BirthdayPicker.Date.HasValue ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetFieldRows(IReadOnlyList<SocialUserField> fields)
        {
            _fieldRows.Clear();
            foreach (var field in fields)
            {
                _fieldRows.Add(new SocialProfileFieldRow(field.Name, field.Value) { Stacked = _fieldRowsStacked });
            }

            RenumberFieldRows();
        }

        private void RenumberFieldRows()
        {
            for (var i = 0; i < _fieldRows.Count; i++)
            {
                _fieldRows[i].Position = i + 1;
            }
        }

        private void ValidateFieldRows()
        {
            foreach (var row in _fieldRows)
            {
                var hasName = Normalize(row.Name).Length > 0;
                var hasValue = Normalize(row.Value).Length > 0;
                row.Error = hasName == hasValue ? ""
                            : LocalizedStrings.Get(hasName ? "SocialProfileFieldNeedsValue" : "SocialProfileFieldNeedsLabel");
            }
        }

        private void UpdateDetailsState()
        {
            LocationRing.Update(SocialProfileEditService.Length(LocationBox.Text), SocialProfileEditService.LocationMaxLength);
            ValidateFieldRows();

            var count = _fieldRows.Count;
            FieldCountText.Text = LocalizedStrings.Format("SocialProfileFieldCountFormat", count, SocialProfileEditService.FieldsMaxCount);
            Windows.UI.Xaml.Automation.AutomationProperties.SetHelpText(AddFieldButton, FieldCountText.Text);

            AddFieldButton.IsEnabled = !_saving && count < SocialProfileEditService.FieldsMaxCount;
            LocationBox.IsReadOnly = _saving;
            BirthdayPicker.IsEnabled = !_saving;
            ClearBirthdayButton.IsEnabled = !_saving;
            FieldRows.IsEnabled = !_saving;
        }

        private void BirthdayPicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            try
            {
                if (sender.Date.HasValue && sender.Date.Value.Date > DateTimeOffset.Now.Date)
                {
                    sender.Date = args.OldDate;
                    return;
                }

                ClearBirthdayButton.Visibility = sender.Date.HasValue ? Visibility.Visible : Visibility.Collapsed;
                if (_loading) return;

                StatusText.Visibility = Visibility.Collapsed;
                UpdateState();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: birthday change failed", ex);
            }
        }

        private void ClearBirthdayButton_Click(object sender, RoutedEventArgs e)
        {
            BirthdayPicker.Date = null;
            BirthdayPicker.Focus(FocusState.Programmatic);
        }

        private void FieldLabel_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            var row = box?.DataContext as SocialProfileFieldRow;
            if (row == null || string.Equals(row.Name, box.Text, StringComparison.Ordinal)) return;

            row.Name = box.Text;
            FieldEdited();
        }

        private void FieldValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            var row = box?.DataContext as SocialProfileFieldRow;
            if (row == null || string.Equals(row.Value, box.Text, StringComparison.Ordinal)) return;

            row.Value = box.Text;
            FieldEdited();
        }

        private void FieldEdited()
        {
            if (_loading) return;

            StatusText.Visibility = Visibility.Collapsed;
            UpdateState();
        }

        private void AddFieldButton_Click(object sender, RoutedEventArgs e)
        {
            if (_saving || _fieldRows.Count >= SocialProfileEditService.FieldsMaxCount) return;

            _fieldRows.Add(new SocialProfileFieldRow("", "") { Stacked = _fieldRowsStacked });
            RenumberFieldRows();
            UpdateState();
            FocusFieldRow(_fieldRows.Count - 1);
        }

        private void RemoveFieldButton_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as SocialProfileFieldRow;
            if (row != null) RemoveFieldRow(row);
        }

        private void RemoveFieldRow(SocialProfileFieldRow row)
        {
            var index = _fieldRows.IndexOf(row);
            if (_saving || index < 0) return;

            _fieldRows.RemoveAt(index);
            RenumberFieldRows();
            UpdateState();

            if (_fieldRows.Count == 0)
            {
                AddFieldButton.Focus(FocusState.Programmatic);
            }
            else
            {
                FocusFieldRow(Math.Min(index, _fieldRows.Count - 1));
            }
        }

        private void MoveFieldRow(SocialProfileFieldRow row, int offset)
        {
            var index = _fieldRows.IndexOf(row);
            var target = index + offset;
            if (_saving || index < 0 || target < 0 || target >= _fieldRows.Count) return;

            _fieldRows.Move(index, target);
            RenumberFieldRows();
            UpdateState();
            FocusFieldRow(target);
        }

        private void FocusFieldRow(int index)
        {
            try
            {
                FieldRows.UpdateLayout();
                var container = FieldRows.ContainerFromIndex(index) as DependencyObject;
                var box = container == null ? null : VisualTreeSearch.FindDescendant<TextBox>(container);
                if (box != null) box.Focus(FocusState.Programmatic);
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: could not focus a field row", ex);
            }
        }

        private void FieldRow_Loaded(object sender, RoutedEventArgs e)
        {
            var grid = sender as Grid;
            if (grid == null || grid.KeyboardAccelerators.Count > 0) return;

            AddFieldRowAccelerator(grid, VirtualKey.Up, -1);
            AddFieldRowAccelerator(grid, VirtualKey.Down, 1);
        }

        private void AddFieldRowAccelerator(Grid grid, VirtualKey key, int offset)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Menu, ScopeOwner = grid };
            accelerator.Invoked += (s, args) =>
            {
                var row = grid.DataContext as SocialProfileFieldRow;
                if (row == null) return;

                args.Handled = true;
                MoveFieldRow(row, offset);
            };
            grid.KeyboardAccelerators.Add(accelerator);
        }

        private void FieldRow_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            try
            {
                var row = (sender as FrameworkElement)?.DataContext as SocialProfileFieldRow;
                if (row == null || _saving) return;

                var index = _fieldRows.IndexOf(row);
                var menu = new MenuFlyout();

                var up = new MenuFlyoutItem { Text = LocalizedStrings.Get("SocialProfileFieldMoveUp"), IsEnabled = index > 0 };
                up.KeyboardAcceleratorTextOverride = LocalizedStrings.Get("SocialProfileFieldMoveUpKeys");
                up.Click += (s, e) => MoveFieldRow(row, -1);
                menu.Items.Add(up);

                var down = new MenuFlyoutItem { Text = LocalizedStrings.Get("SocialProfileFieldMoveDown"), IsEnabled = index < _fieldRows.Count - 1 };
                down.KeyboardAcceleratorTextOverride = LocalizedStrings.Get("SocialProfileFieldMoveDownKeys");
                down.Click += (s, e) => MoveFieldRow(row, 1);
                menu.Items.Add(down);

                menu.Items.Add(new MenuFlyoutSeparator());

                var remove = new MenuFlyoutItem { Text = LocalizedStrings.Get("SocialProfileFieldRemove") };
                remove.Click += (s, e) => RemoveFieldRow(row);
                menu.Items.Add(remove);

                Windows.Foundation.Point point;
                if (args.TryGetPosition(sender, out point))
                {
                    menu.ShowAt(sender, point);
                }
                else
                {
                    menu.ShowAt(sender as FrameworkElement);
                }

                args.Handled = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialProfileEditor: could not open the field menu", ex);
            }
        }

        private void FieldRows_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var stacked = e.NewSize.Width < FieldRowsStackBelowWidth;
            if (stacked == _fieldRowsStacked) return;

            _fieldRowsStacked = stacked;
            foreach (var row in _fieldRows)
            {
                row.Stacked = stacked;
            }
        }
    }
}
