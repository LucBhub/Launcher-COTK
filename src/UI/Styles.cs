using System.Windows;
using System.Windows.Markup;

namespace COTK.Next.UI;

/// <summary>Styles implicites (barres de défilement fines, Expander discret, info-bulles sombres).
/// Définis en XAML textuel parsé au démarrage : pas de fichier .xaml à compiler.</summary>
internal static class Styles
{
    private const string Xaml = """
    <ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

      <Style x:Key='InvisibleRepeat' TargetType='RepeatButton'>
        <Setter Property='Focusable' Value='False'/>
        <Setter Property='IsTabStop' Value='False'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>

      <Style TargetType='ScrollBar'>
        <Setter Property='Background' Value='Transparent'/>
        <Setter Property='Width' Value='10'/>
        <Setter Property='MinWidth' Value='10'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='Transparent' SnapsToDevicePixels='True'>
                <Track x:Name='PART_Track' IsDirectionReversed='True'>
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Style='{StaticResource InvisibleRepeat}' Command='ScrollBar.PageUpCommand'/>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Style='{StaticResource InvisibleRepeat}' Command='ScrollBar.PageDownCommand'/>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb>
                      <Thumb.Template>
                        <ControlTemplate TargetType='Thumb'>
                          <Border x:Name='bar' CornerRadius='3' Background='#2effffff' Margin='3,2,3,2'/>
                          <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bar' Property='Background' Value='#5cffffff'/></Trigger>
                            <Trigger Property='IsDragging' Value='True'><Setter TargetName='bar' Property='Background' Value='#99ff6a2b'/></Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property='Orientation' Value='Horizontal'>
                  <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
        <Style.Triggers>
          <Trigger Property='Orientation' Value='Horizontal'>
            <Setter Property='Width' Value='Auto'/>
            <Setter Property='MinWidth' Value='0'/>
            <Setter Property='Height' Value='10'/>
          </Trigger>
        </Style.Triggers>
      </Style>

      <Style TargetType='ToolTip'>
        <Setter Property='Background' Value='#1a1a20'/>
        <Setter Property='Foreground' Value='#f5f5f7'/>
        <Setter Property='BorderBrush' Value='#3a3a44'/>
        <Setter Property='Padding' Value='10,6'/>
        <Setter Property='FontSize' Value='12'/>
      </Style>
    </ResourceDictionary>
    """;

    public static void Install()
    {
        var dictionary = (ResourceDictionary)XamlReader.Parse(Xaml);
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }
}
