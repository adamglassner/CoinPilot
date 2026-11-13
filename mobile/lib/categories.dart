import 'package:flutter/material.dart';

/// The same built-in categories as WillowWallet for Windows.
const spendingCategories = [
  'Groceries', 'Dining out', 'Housing', 'Utilities', 'Transportation', 'Shopping',
  'Entertainment', 'Health', 'Personal care', 'Kids', 'Pets', 'Travel', 'Gifts', 'Subscriptions',
  'Debt payments', 'Savings', 'Taxes', 'Other',
];

const incomeCategories = ['Paycheck', 'Side income', 'Refund', 'Interest', 'Gift received', 'Other income'];

const defaultAccounts = ['Checking', 'Savings', 'Credit card', 'Cash'];

const _icons = <String, IconData>{
  'Groceries': Icons.shopping_cart_outlined,
  'Dining out': Icons.restaurant_rounded,
  'Housing': Icons.home_outlined,
  'Utilities': Icons.bolt_rounded,
  'Transportation': Icons.directions_car_outlined,
  'Shopping': Icons.shopping_bag_outlined,
  'Entertainment': Icons.movie_outlined,
  'Health': Icons.favorite_border_rounded,
  'Personal care': Icons.spa_outlined,
  'Kids': Icons.child_care_rounded,
  'Pets': Icons.pets_rounded,
  'Travel': Icons.flight_takeoff_rounded,
  'Gifts': Icons.card_giftcard_rounded,
  'Subscriptions': Icons.autorenew_rounded,
  'Debt payments': Icons.credit_card_rounded,
  'Savings': Icons.savings_outlined,
  'Taxes': Icons.receipt_long_outlined,
  'Other': Icons.category_outlined,
  'Paycheck': Icons.payments_outlined,
  'Side income': Icons.work_outline_rounded,
  'Refund': Icons.undo_rounded,
  'Interest': Icons.percent_rounded,
  'Gift received': Icons.redeem_rounded,
  'Other income': Icons.add_circle_outline_rounded,
};

IconData categoryIcon(String category) => _icons[category] ?? Icons.label_outline_rounded;
