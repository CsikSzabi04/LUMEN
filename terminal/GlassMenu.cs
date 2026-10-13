// Közös, sötét üveg stílusú jobb klikkes menü (ContextMenu / MenuItem) a GlassControlhoz, a Terminalhoz és a GlassDockhoz.
// Egy implicit stílus-szótár: ahol egyszer ráteszed a ThemeApp-ra, minden ContextMenu automatikusan ilyen lesz.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace GlassTerm
{
    public class MenuEntry { public string Header, Gesture; public Action Click; public bool Sep, Disabled, Checked; public List<MenuEntry> Sub; }

    public static class GlassMenu
    {
        const string Xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Margin='8,4,8,4' Background='#33FFFFFF'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='FontFamily' Value='Inter, Segoe UI'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Margin='10' CornerRadius='12' Background='#F21D1D20' BorderBrush='#55FFFFFF' BorderThickness='1' Padding='5' MinWidth='190'>
            <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='4' Opacity='0.5' Direction='270'/></Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='#FFF2F2F7'/>
    <Setter Property='FontFamily' Value='Inter, Segoe UI'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' CornerRadius='7' Padding='8,6,10,6' Background='Transparent' SnapsToDevicePixels='True'>
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='14'/></Grid.ColumnDefinitions>
              <TextBlock x:Name='Chk' Text='&#x2713;' Visibility='Collapsed' FontWeight='Bold' VerticalAlignment='Center' Foreground='{TemplateBinding Foreground}'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='#88FFFFFF' FontSize='11.5' Margin='26,0,0,0' VerticalAlignment='Center'/>
              <TextBlock x:Name='Arr' Grid.Column='3' Text='&#x203A;' Visibility='Collapsed' FontSize='16' VerticalAlignment='Center' HorizontalAlignment='Right' Foreground='{TemplateBinding Foreground}'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-6' VerticalOffset='-8' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade' IsOpen='{Binding Path=IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border CornerRadius='12' Background='#F21D1D20' BorderBrush='#55FFFFFF' BorderThickness='1' Padding='5' Margin='10' MinWidth='170'>
                  <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='4' Opacity='0.5' Direction='270'/></Border.Effect>
                  <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='#FF0A84FF'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='#55FFFFFF'/></Trigger>
            <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chk' Property='Visibility' Value='Visible'/></Trigger>
            <Trigger Property='HasItems' Value='True'><Setter TargetName='Arr' Property='Visibility' Value='Visible'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        static ResourceDictionary theme;
        // az egész alkalmazásra (minden ablak, minden ContextMenu)
        public static void ApplyTheme()
        {
            try { if (theme == null) theme = (ResourceDictionary)XamlReader.Parse(Xaml); if (Application.Current != null && !Application.Current.Resources.MergedDictionaries.Contains(theme)) Application.Current.Resources.MergedDictionaries.Add(theme); } catch { }
        }

        public static MenuEntry Item(string header, Action click) { return new MenuEntry { Header = header, Click = click }; }
        public static MenuEntry Item(string header, string gesture, Action click) { return new MenuEntry { Header = header, Gesture = gesture, Click = click }; }
        public static MenuEntry Off(string header) { return new MenuEntry { Header = header, Disabled = true }; }
        public static MenuEntry Check(string header, bool on, Action click) { return new MenuEntry { Header = header, Checked = on, Click = click }; }
        public static MenuEntry Sep() { return new MenuEntry { Sep = true }; }
        public static MenuEntry Menu(string header, params MenuEntry[] sub) { return new MenuEntry { Header = header, Sub = new List<MenuEntry>(sub) }; }

        static void Fill(ItemsControl target, IEnumerable<MenuEntry> items)
        {
            foreach (MenuEntry e in items)
            {
                if (e == null) continue;
                if (e.Sep) { target.Items.Add(new Separator()); continue; }
                MenuItem mi = new MenuItem(); mi.Header = e.Header; if (e.Gesture != null) mi.InputGestureText = e.Gesture; mi.IsEnabled = !e.Disabled; if (e.Checked) { mi.IsCheckable = false; mi.IsChecked = true; }
                if (e.Sub != null) Fill(mi, e.Sub);
                else if (e.Click != null) { Action a = e.Click; mi.Click += delegate { try { a(); } catch { } }; }
                target.Items.Add(mi);
            }
        }

        public static ContextMenu Create(params MenuEntry[] items)
        {
            ApplyTheme(); ContextMenu cm = new ContextMenu(); Fill(cm, items); return cm;
        }

        // megnyitás az egér helyén
        public static void Show(FrameworkElement owner, params MenuEntry[] items)
        {
            ContextMenu cm = Create(items); cm.PlacementTarget = owner; cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint; cm.IsOpen = true;
        }

        // egy elemhez "mindig a jobb klikk" menüt köt; az építő minden megnyitáskor újra lefut (az aktuális állapotot tükrözi)
        // szövegmezők saját menüje (Kivágás, Másolás, Beillesztés...)
        public static void ForTextBox(TextBox tb)
        {
            tb.ContextMenuOpening += delegate (object o, ContextMenuEventArgs e)
            {
                e.Handled = true; bool ro = tb.IsReadOnly, sel = tb.SelectionLength > 0;
                Show(tb,
                    ro || !sel ? Off(Loc.T("Kivágás")) : Item(Loc.T("Kivágás"), "Ctrl+X", delegate { tb.Cut(); }),
                    sel ? Item(Loc.T("Másolás"), "Ctrl+C", delegate { tb.Copy(); }) : Off(Loc.T("Másolás")),
                    ro ? Off(Loc.T("Beillesztés")) : Item(Loc.T("Beillesztés"), "Ctrl+V", delegate { tb.Paste(); }),
                    Sep(), Item(Loc.T("Összes kijelölése"), "Ctrl+A", delegate { tb.SelectAll(); }),
                    tb.Text.Length > 0 && !(ro && false) ? Item(Loc.T("Törlés"), delegate { tb.Clear(); }) : Off(Loc.T("Törlés")));
            };
        }

        public static void Attach(FrameworkElement el, Func<MenuEntry[]> build)
        {
            el.MouseRightButtonUp += delegate (object o, System.Windows.Input.MouseButtonEventArgs e) { MenuEntry[] items = build(); if (items != null && items.Length > 0) { Show(el, items); e.Handled = true; } };
        }
    }
}
