import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Icon, type IconName } from './icon';

function render(name: IconName, label: string | null = null): HTMLElement {
  const fixture = TestBed.createComponent(Icon);
  fixture.componentRef.setInput('name', name);
  fixture.componentRef.setInput('label', label);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Icon', () => {
  it('dessine chaque forme dans le trait du texte', () => {
    const host = render('music');
    const svg = host.querySelector('svg');

    expect(svg?.getAttribute('stroke')).toBe('currentColor');
    expect(host.querySelectorAll('circle')).toHaveLength(3);
    // Le point central est plein, les deux cercles sont au trait.
    expect([...host.querySelectorAll('circle')].map((c) => c.getAttribute('fill'))).toEqual([
      null,
      null,
      'currentColor',
    ]);
  });

  it('est décorative sans libellé', () => {
    const host = render('book');

    expect(host.getAttribute('aria-hidden')).toBe('true');
    expect(host.getAttribute('role')).toBeNull();
  });

  it('se lit comme une image quand elle porte un libellé', () => {
    const host = render('book', 'Livre');

    expect(host.getAttribute('role')).toBe('img');
    expect(host.getAttribute('aria-label')).toBe('Livre');
    expect(host.getAttribute('aria-hidden')).toBeNull();
  });
});
